using System.Reflection;
using Gneedle.Inject;
using Assembly = Gneedle.Inject.Assembly;

namespace Gneedle.Aspect;

public sealed class AssemblyInject : Microsoft.Build.Utilities.Task
{
    /// <summary>
    /// The members which the injectors of a type are looked for on: every member which the type declares, whichever way
    /// a caller could reach it, because a member which no injector names is left alone either way.<para/>
    /// Only the members which the type declares itself are walked, because an injector is applied where its member is
    /// declared. A member which a base type declares is walked with that base type, which the assembly holds as well
    /// when the base type is one of its own, and a member of a base type which another assembly declares cannot be
    /// written to from here at all.
    /// </summary>
    private const BindingFlags InjectedMembers = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                                               | BindingFlags.Static | BindingFlags.DeclaredOnly;

    /// <summary>
    /// The interfaces which an attribute implements to be asked to inject into a type.<para/>
    /// Every one of them is looked for, which the kinds are told apart by afterwards: looking for the first alone left
    /// the attributes of the other three on a type, where they were passed over without a word because nothing had
    /// asked for them.
    /// </summary>
    private static readonly Type[] TypeInjectors = [typeof(ITypeInjector), typeof(IClassInjector), typeof(IStructInjector), typeof(IEnumInjector)];

    [Required] public string ProjectPath { get; private set; }
    [Required] public string TargetPath { get; private set; }

    /// <summary>
    /// Whether the attributes which the injectors are read from, and the reference to the weaver which they name, are
    /// kept in the assembly which is woven.<para/>
    /// They are removed by default, so that the assembly which was woven does not carry the weaver. A project which
    /// declares its attributes for another project to weave with keeps them, which it asks for with the property
    /// <c>KeepWeaver</c>. The value is read as the text of that property, so that a project which was never given
    /// one keeps nothing.
    /// </summary>
    public string? KeepWeaver { get; set; }

    public override bool Execute()
    {
        var project = ProjectRootElement.Open(ProjectPath);
        if (project == null) return false;

        if (project.VerifyAspectDisable()) return true;

        Log.LogMessageFromText($"Inject assembly: {TargetPath}", MessageImportance.High);
        if (InjectAssemblies(TargetPath, KeepsTheWeaver()))
        {
            Log.LogMessageFromText($"Inject assembly: {TargetPath} success.", MessageImportance.High);
        }
        else
        {
            Log.LogMessageFromText($"Inject assembly: {TargetPath} no changes.", MessageImportance.High);
        }

        // The project is only read, to tell whether the aspect is disabled, so it is not saved back. A member which an
        // injector named and the assembly does not hold is reported as an error, and the task fails with it rather than
        // reporting a build which carried on.
        return !Log.HasLoggedErrors;
    }

    /// <summary>
    /// Whether the project asked for the weaver to be kept in the assembly which is woven.
    /// </summary>
    private bool KeepsTheWeaver() => string.Equals(KeepWeaver, "true", StringComparison.OrdinalIgnoreCase);

    private bool InjectAssemblies(string assemblyPath, bool keepsTheWeaver)
    {
        // The reflection assembly is loaded from the bytes rather than from the path. Loading it by path takes the file
        // for itself, and the file is held open for the write which follows, so the two cannot share it.
        var runtimeAssembly = AssemblyLoader.LoadFromBytes(File.ReadAllBytes(assemblyPath));

        // The assembly is written back through the very stream it was read from, and that stream stays open until the
        // assembly is disposed, so it is saved before the end of this scope. Without the write the whole injection is
        // discarded.
        using var assembly = Assembly.Read(assemblyPath);
        var assemblyHandler = new AssemblyHandler(assembly);
        var dirty = ProcessAssembleInjector(assemblyHandler, runtimeAssembly);
        foreach (var type in runtimeAssembly.GetTypes())
        {
            var typeHandler = assemblyHandler.GetType(type);
            if (typeHandler == null!)
            {
                Log.LogError($"Type '{type.FullName}' not found in assembly '{runtimeAssembly.FullName}'.");
                continue;
            }

            dirty |= ProcessTypeInjector(assemblyHandler, type);
            if (type.IsClass || type is {IsValueType: true, IsEnum: false})
            {
                if (typeHandler is IMethodContainer methodContainer)
                {
                    foreach (var method in type.GetMethods(InjectedMembers))
                    {
                        dirty |= ProcessMethodInjector(methodContainer, type, method);
                    }
                }

                if (typeHandler is IFieldContainer fieldContainer)
                {
                    foreach (var field in type.GetFields(InjectedMembers))
                    {
                        dirty |= ProcessFieldInjector(fieldContainer, type, field);
                    }
                }

                if (typeHandler is IPropertyContainer propertyContainer)
                {
                    foreach (var property in type.GetProperties(InjectedMembers))
                    {
                        dirty |= ProcessPropertyInjector(propertyContainer, type, property);
                    }
                }
            }
        }

        // The injectors are read from attributes which the project declares, and those attributes name the weaver, so
        // the weaver is removed from the assembly once they have been applied to it. A project which declares them for
        // another one to weave with keeps them, and keeps the weaver which they name.
        if (!keepsTheWeaver) dirty |= assemblyHandler.RemoveTheWeaver();

        if (dirty) assembly.SaveTo(assemblyPath);

        return dirty;
    }

    private bool ProcessAssembleInjector(AssemblyHandler assemblyHandler, System.Reflection.Assembly runtimeAssembly)
    {
        var injectors = runtimeAssembly.GetCustomAttributes(inherit: false)
                                       .Where(static item => item is Attribute attr && attr.GetType().GetInterfaces().Contains(typeof(IAssemblyInjector)))
                                       .Cast<IAssemblyInjector>()
                                       .ToArray();
        if (injectors.Length == 0) return false;
        foreach (var injector in injectors)
        {
            injector.Inject(runtimeAssembly, assemblyHandler);
        }

        return true;
    }

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
                    Log.LogError($"Type '{type.FullName}' is not a class, which '{typeAttribute.GetType().FullName}' injects into.");
                    continue;
                }

                classInjector.Inject(type, classHandler);
                dirty = true;
            }

            if (typeAttribute is IStructInjector structInjector)
            {
                if (typeHandler is not IStructHandler structHandler)
                {
                    Log.LogError($"Type '{type.FullName}' is not a struct, which '{typeAttribute.GetType().FullName}' injects into.");
                    continue;
                }

                structInjector.Inject(type, structHandler);
                dirty = true;
            }

            if (typeAttribute is IEnumInjector enumInjector)
            {
                if (typeHandler is not IEnumHandler enumHandler)
                {
                    Log.LogError($"Type '{type.FullName}' is not an enum, which '{typeAttribute.GetType().FullName}' injects into.");
                    continue;
                }

                enumInjector.Inject(type, enumHandler);
                dirty = true;
            }
        }

        return dirty;
    }

    private bool ProcessMethodInjector(IMethodContainer typeHandler, Type runtimeType, MethodInfo methodInfo)
    {
        if (methodInfo.GetCustomAttributes(inherit: false)
                      .Where(static item => item is Attribute attr && attr.GetType().GetInterfaces().Contains(typeof(IMethodInjector)))
                      .Cast<IMethodInjector>()
                      .ToArray() is not {Length: > 0} injectors) return false;

        // The assembly is written back only when something was injected into it, so an injector which found nothing to
        // inject into is not counted as a change: the member it names was reported instead.
        var injected = false;
        foreach (var injector in injectors)
        {
            var methodHandler = typeHandler.GetMethod(methodInfo.Name, methodInfo.GetParameters().GetITypes());
            if (methodHandler == null)
            {
                Log.LogError($"Method '{methodInfo.Name}' not found in type '{runtimeType.FullName}'.");
                continue;
            }

            injector.Inject(methodInfo, methodHandler);
            injected = true;
        }

        return injected;
    }

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
                Log.LogError($"Field '{fieldInfo.Name}' not found in type '{runtimeType.FullName}'.");
                continue;
            }

            injector.Inject(fieldInfo, fieldHandler);
            injected = true;
        }

        return injected;
    }

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
                Log.LogError($"Property '{propertyInfo.Name}' not found in type '{runtimeType.FullName}'.");
                continue;
            }

            injector.Inject(propertyInfo, propertyHandler);
            injected = true;
        }

        return injected;
    }
}