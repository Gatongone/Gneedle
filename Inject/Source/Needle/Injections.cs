using System.Reflection;

namespace Gneedle.Inject;

/// <summary>
/// The injectors which an assembly declares, applied to the assembly itself.<para/>
/// An injector is an attribute which implements one of the interfaces of this library, and which says what to do with
/// the member it is put on. Applying them is the last step of a weaver: the assembly is read as an image of bytes, its
/// members are looked up in that image, and each injector is called with the handler of the member it names.
/// </summary>
/// <remarks>
/// The assembly is given as the one which was loaded from the image, because how an assembly is loaded belongs to
/// whoever loads it: a build which reads an assembly from a file, a tool which holds it in memory, and an editor which
/// compiles one each resolve the assemblies they refer to in their own way.
/// </remarks>
public static class Injections
{
    /// <summary>
    /// Apply the injectors which <paramref name="assembly"/> declares to the image it was loaded from.
    /// </summary>
    /// <param name="assembly">The assembly whose attributes are the injectors, which is the image being woven.</param>
    /// <param name="image">The bytes of the image which the assembly was loaded from.</param>
    /// <param name="removesTheWeaver">Whether the attributes and the reference to this library are taken back out. They are removed by default, which leaves the woven assembly standing alone.</param>
    /// <param name="reportError">Where a member which an injector names and the assembly does not hold is reported, or null when nothing reports it.</param>
    /// <returns>Whether the assembly was changed, and the image which holds the result.</returns>
    public static (bool Changed, byte[] Image) Apply(System.Reflection.Assembly assembly, byte[] image,
                                                     bool removesTheWeaver = true, Action<string>? reportError = null)
        => new Injection(assembly, image, removesTheWeaver, reportError).Run();

    /// <summary>
    /// One run of the injectors of one assembly.
    /// </summary>
    /// <param name="assembly">The assembly whose attributes are the injectors.</param>
    /// <param name="image">The bytes of the image which the assembly was loaded from.</param>
    /// <param name="removesTheWeaver">Whether the attributes and the reference to this library are taken back out.</param>
    /// <param name="reportError">Where a member which an injector names and the assembly does not hold is reported.</param>
    private sealed class Injection(System.Reflection.Assembly assembly, byte[] image, bool removesTheWeaver, Action<string>? reportError)
    {
        /// <summary>
        /// The members which the injectors of a type are looked for on: every member which the type declares, whichever
        /// way a caller could reach it, because a member which no injector names is left alone either way.<para/>
        /// Only the members which the type declares itself are walked, because an injector is applied where its member
        /// is declared. A member which a base type declares is walked with that base type, which the assembly holds as
        /// well when the base type is one of its own, and a member of a base type which another assembly declares cannot
        /// be written to from here at all.
        /// </summary>
        private const BindingFlags InjectedMembers = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                                                   | BindingFlags.Static | BindingFlags.DeclaredOnly;

        /// <summary>
        /// The interfaces which an attribute implements to be asked to inject into a type.<para/>
        /// Every one of them is looked for, which the kinds are told apart by afterwards: looking for the first alone
        /// leaves the attributes of the other three on a type, where they are passed over without a word because nothing
        /// asked for them.
        /// </summary>
        private static readonly Type[] TypeInjectors = [typeof(ITypeInjector), typeof(IClassInjector), typeof(IStructInjector), typeof(IEnumInjector)];

        /// <summary>
        /// Apply every injector of the assembly.
        /// </summary>
        public (bool Changed, byte[] Image) Run()
        {
            // The assembly is read as an image of bytes and written back as one, so that the caller keeps the file to
            // itself: a reader which holds the file leaves the write which follows nowhere to go.
            using var stream = new MemoryStream(image);
            using var target = Assembly.Read(stream);

            var handler = new AssemblyHandler(target);
            var changed = ProcessAssembleInjector(handler);
            foreach (var type in assembly.GetTypes())
            {
                // One type which cannot be woven does not take the rest of the assembly with it. What went wrong is
                // reported, and whoever drives the library decides what a report is worth: a build reports it as an
                // error and fails while the assembly it wove is discarded, and a driver which has an assembly to answer
                // with keeps the one it was given.
                try
                {
                    changed |= ProcessType(handler, type);
                }
                catch (Exception exception)
                {
                    Report($"Type '{type.FullName}' could not be woven. {exception.Message}");
                }
            }

            // The injectors are read from attributes which the assembly declares, and those attributes name the weaver,
            // so the weaver is removed from the assembly once they have been applied to it. An assembly which declares
            // them for another one to weave with keeps them, and keeps the weaver which they name.
            if (removesTheWeaver) changed |= handler.RemoveTheWeaver();

            if (!changed) return (false, image);

            using var result = new MemoryStream();
            target.SaveTo(result);
            return (true, result.ToArray());
        }

        /// <summary>
        /// Report a member which an injector named and the assembly does not hold.
        /// </summary>
        /// <param name="message">What is reported.</param>
        private void Report(string message) => reportError?.Invoke(message);

        /// <summary>
        /// Apply the injectors of one type of the assembly.
        /// </summary>
        /// <param name="handler">Handler of the assembly which the type belongs to.</param>
        /// <param name="type">The type which the injectors of it are applied to.</param>
        /// <returns>Whether the type was changed.</returns>
        private bool ProcessType(AssemblyHandler handler, Type type)
        {
            var changed = false;
            var typeHandler = handler.GetType(type);
            if (typeHandler == null!)
            {
                Report($"Type '{type.FullName}' not found in assembly '{assembly.FullName}'.");
                return false;
            }

            changed |= ProcessTypeInjector(handler, type);
            if (!type.IsClass && type is not {IsValueType: true, IsEnum: false}) return changed;

            if (typeHandler is IMethodContainer methodContainer)
            {
                foreach (var method in type.GetMethods(InjectedMembers))
                {
                    changed |= ProcessMethodInjector(methodContainer, type, method);
                }
            }

            if (typeHandler is IFieldContainer fieldContainer)
            {
                foreach (var field in type.GetFields(InjectedMembers))
                {
                    changed |= ProcessFieldInjector(fieldContainer, type, field);
                }
            }

            if (typeHandler is IPropertyContainer propertyContainer)
            {
                foreach (var property in type.GetProperties(InjectedMembers))
                {
                    changed |= ProcessPropertyInjector(propertyContainer, type, property);
                }
            }

            return changed;
        }

        /// <summary>
        /// Apply the injectors which the assembly itself declares, which are the attributes put on the assembly rather
        /// than on a member of it.
        /// </summary>
        /// <param name="assemblyHandler">Handler of the assembly which the injectors are applied to.</param>
        /// <returns>Whether the assembly declares an injector at all.</returns>
        private bool ProcessAssembleInjector(AssemblyHandler assemblyHandler)
        {
            var injectors = assembly.GetCustomAttributes(inherit: false)
                                    .Where(static item => item is Attribute attr && attr.GetType().GetInterfaces().Contains(typeof(IAssemblyInjector)))
                                    .Cast<IAssemblyInjector>()
                                    .ToArray();
            if (injectors.Length == 0) return false;
            foreach (var injector in injectors)
            {
                injector.Inject(assembly, assemblyHandler);
            }

            return true;
        }

        /// <summary>
        /// Apply the injectors which one type declares, each of them asked of the kind of handler it injects into, so
        /// that one which is put on a type of another kind is reported rather than passed over.
        /// </summary>
        /// <param name="assemblyHandler">Handler of the assembly which the type belongs to.</param>
        /// <param name="type">The type whose injectors are applied.</param>
        /// <returns>Whether an injector was applied.</returns>
        private bool ProcessTypeInjector(AssemblyHandler assemblyHandler, Type type)
        {
            var dirty = false;
            var typeAttributes = type.GetCustomAttributes(inherit: false)
                                     .Where(static item => item is Attribute attr && TypeInjectors.Any(injector => injector.IsInstanceOfType(attr)))
                                     .Cast<Attribute>()
                                     .ToArray();

            foreach (var typeAttribute in typeAttributes)
            {
                var typeHandler = assemblyHandler.GetType(type);
                if (typeHandler == null!)
                {
                    continue;
                }

                if (typeAttribute is ITypeInjector typeInjector)
                {
                    typeInjector.Inject(type, typeHandler);
                    dirty = true;
                }

                // A type injector applies to any type, while the three below apply to the kind which they name and to no
                // other. One which is asked of a type of another kind is reported rather than passed over, because the
                // injection it stands for does not happen, and a build which carried on would say that it had.
                if (typeAttribute is IClassInjector classInjector)
                {
                    if (typeHandler is not IClassHandler classHandler)
                    {
                        Report($"Type '{type.FullName}' is not a class, which '{typeAttribute.GetType().FullName}' injects into.");
                        continue;
                    }

                    classInjector.Inject(type, classHandler);
                    dirty = true;
                }

                if (typeAttribute is IStructInjector structInjector)
                {
                    if (typeHandler is not IStructHandler structHandler)
                    {
                        Report($"Type '{type.FullName}' is not a struct, which '{typeAttribute.GetType().FullName}' injects into.");
                        continue;
                    }

                    structInjector.Inject(type, structHandler);
                    dirty = true;
                }

                if (typeAttribute is IEnumInjector enumInjector)
                {
                    if (typeHandler is not IEnumHandler enumHandler)
                    {
                        Report($"Type '{type.FullName}' is not an enum, which '{typeAttribute.GetType().FullName}' injects into.");
                        continue;
                    }

                    enumInjector.Inject(type, enumHandler);
                    dirty = true;
                }
            }

            return dirty;
        }

        /// <summary>
        /// Apply the injectors which one method carries, which are looked up in the assembly by the name and the
        /// parameters of the method rather than by the method itself.
        /// </summary>
        /// <param name="typeHandler">Handler of the type which declares the method.</param>
        /// <param name="runtimeType">The type which the method belongs to, which what is reported names.</param>
        /// <param name="methodInfo">The method whose injectors are applied.</param>
        /// <returns>Whether an injector was applied.</returns>
        private bool ProcessMethodInjector(IMethodContainer typeHandler, Type runtimeType, MethodInfo methodInfo)
        {
            if (methodInfo.GetCustomAttributes(inherit: false)
                          .Where(static item => item is Attribute attr && attr.GetType().GetInterfaces().Contains(typeof(IMethodInjector)))
                          .Cast<IMethodInjector>()
                          .ToArray() is not {Length: > 0} injectors) return false;

            // The assembly is written back only when something was injected into it, so an injector which found nothing
            // to inject into is not counted as a change: the member it names was reported instead.
            var injected = false;
            foreach (var injector in injectors)
            {
                var methodHandler = typeHandler.GetMethod(methodInfo.Name, methodInfo.GetParameters().GetITypes());
                if (methodHandler == null)
                {
                    Report($"Method '{methodInfo.Name}' not found in type '{runtimeType.FullName}'.");
                    continue;
                }

                injector.Inject(methodInfo, methodHandler);
                injected = true;
            }

            return injected;
        }

        /// <summary>
        /// Apply the injectors which one field carries, which are looked up in the assembly by the name of the field
        /// rather than by the field itself.
        /// </summary>
        /// <param name="typeHandler">Handler of the type which declares the field.</param>
        /// <param name="runtimeType">The type which the field belongs to, which what is reported names.</param>
        /// <param name="fieldInfo">The field whose injectors are applied.</param>
        /// <returns>Whether an injector was applied.</returns>
        private bool ProcessFieldInjector(IFieldContainer typeHandler, Type runtimeType, FieldInfo fieldInfo)
        {
            if (fieldInfo.GetCustomAttributes(inherit: false)
                         .Where(static item => item is Attribute attr && attr.GetType().GetInterfaces().Contains(typeof(IFieldInjector)))
                         .Cast<IFieldInjector>()
                         .ToArray() is not {Length: > 0} injectors) return false;

            var injected = false;
            foreach (var injector in injectors)
            {
                var fieldHandler = typeHandler.GetField(fieldInfo.Name);
                if (fieldHandler == null)
                {
                    Report($"Field '{fieldInfo.Name}' not found in type '{runtimeType.FullName}'.");
                    continue;
                }

                injector.Inject(fieldInfo, fieldHandler);
                injected = true;
            }

            return injected;
        }

        /// <summary>
        /// Apply the injectors which one property carries, which are looked up in the assembly by the name of the
        /// property rather than by the property itself.
        /// </summary>
        /// <param name="typeHandler">Handler of the type which declares the property.</param>
        /// <param name="runtimeType">The type which the property belongs to, which what is reported names.</param>
        /// <param name="propertyInfo">The property whose injectors are applied.</param>
        /// <returns>Whether an injector was applied.</returns>
        private bool ProcessPropertyInjector(IPropertyContainer typeHandler, Type runtimeType, PropertyInfo propertyInfo)
        {
            if (propertyInfo.GetCustomAttributes(inherit: false)
                            .Where(static item => item is Attribute attr && attr.GetType().GetInterfaces().Contains(typeof(IPropertyInjector)))
                            .Cast<IPropertyInjector>()
                            .ToArray() is not {Length: > 0} injectors) return false;

            var injected = false;
            foreach (var injector in injectors)
            {
                var propertyHandler = typeHandler.GetProperty(propertyInfo.Name);
                if (propertyHandler == null)
                {
                    Report($"Property '{propertyInfo.Name}' not found in type '{runtimeType.FullName}'.");
                    continue;
                }

                injector.Inject(propertyInfo, propertyHandler);
                injected = true;
            }

            return injected;
        }
    }
}
