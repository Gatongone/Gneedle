using System.Reflection;
using Gneedle.Inject;
using Assembly = Gneedle.Inject.Assembly;

namespace Gneedle.Aspect;

public sealed class AssemblyInject : Microsoft.Build.Utilities.Task
{
    [Required] public string ProjectPath { get; private set; }
    [Required] public string TargetPath { get; private set; }

    public override bool Execute()
    {
        var project = ProjectRootElement.Open(ProjectPath);
        if (project == null) return false;

        if (project.VerifyAspectDisable()) return true;

        Log.LogMessageFromText($"Inject assembly: {TargetPath}", MessageImportance.High);
        if (InjectAssemblies(TargetPath))
        {
            Log.LogMessageFromText($"Inject assembly: {TargetPath} success.", MessageImportance.High);
        }
        else
        {
            Log.LogMessageFromText($"Inject assembly: {TargetPath} no changes.", MessageImportance.High);
        }

        // The project is only read, to tell whether the aspect is disabled, so it is not saved back.
        return true;
    }

    private bool InjectAssemblies(string assemblyPath)
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
                    foreach (var method in type.GetMethods())
                    {
                        dirty |= ProcessMethodInjector(methodContainer, type, method);
                    }
                }

                if (typeHandler is IFieldContainer fieldContainer)
                {
                    foreach (var field in type.GetFields())
                    {
                        dirty |= ProcessFieldInjector(fieldContainer, type, field);
                    }
                }

                if (typeHandler is IPropertyContainer propertyContainer)
                {
                    foreach (var property in type.GetProperties())
                    {
                        dirty |= ProcessPropertyInjector(propertyContainer, type, property);
                    }
                }
            }
        }

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
                                 .Where(static item => item is Attribute attr && attr.GetType().GetInterfaces().Contains(typeof(ITypeInjector)))
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

            if (typeAttribute is IClassInjector classInjector)
            {
                if (typeHandler is IClassHandler classHandler)
                {
                    classInjector.Inject(type, classHandler);
                }

                dirty = true;
            }

            if (typeAttribute is IStructInjector structInjector)
            {
                if (typeHandler is IStructHandler structHandler)
                {
                    structInjector.Inject(type, structHandler);
                }

                dirty = true;
            }

            if (typeAttribute is IEnumInjector enumInjector)
            {
                if (typeHandler is IEnumHandler enumHandler)
                {
                    enumInjector.Inject(type, enumHandler);
                }

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

        foreach (var injector in injectors)
        {
            var methodHandler = typeHandler.GetMethod(methodInfo.Name, methodInfo.GetParameters().GetITypes());
            if (methodHandler == null)
            {
                Log.LogError($"Method '{methodInfo.Name}' not found in type '{runtimeType.FullName}'.");
                continue;
            }

            injector.Inject(methodInfo, methodHandler);
        }

        return true;
    }

    private bool ProcessFieldInjector(IFieldContainer typeHandler, Type runtimeType, FieldInfo fieldInfo)
    {
        if (fieldInfo.GetCustomAttributes(inherit: false)
                     .Where(static item => item is Attribute attr && attr.GetType().GetInterfaces().Contains(typeof(IFieldInjector)))
                     .Cast<IFieldInjector>()
                     .ToArray() is not {Length: > 0} injectors) return false;

        foreach (var injector in injectors)
        {
            var fieldHandler = typeHandler.GetField(fieldInfo.Name);
            if (fieldHandler == null)
            {
                Log.LogError($"Field '{fieldInfo.Name}' not found in type '{runtimeType.FullName}'.");
                continue;
            }

            injector.Inject(fieldInfo, fieldHandler);
        }

        return true;
    }

    private bool ProcessPropertyInjector(IPropertyContainer typeHandler, Type runtimeType, PropertyInfo propertyInfo)
    {
        if (propertyInfo.GetCustomAttributes(inherit: false)
                        .Where(static item => item is Attribute attr && attr.GetType().GetInterfaces().Contains(typeof(IPropertyInjector)))
                        .Cast<IPropertyInjector>()
                        .ToArray() is not {Length: > 0} injectors) return false;

        foreach (var injector in injectors)
        {
            var propertyHandler = typeHandler.GetProperty(propertyInfo.Name);
            if (propertyHandler == null)
            {
                Log.LogError($"Property '{propertyInfo.Name}' not found in type '{runtimeType.FullName}'.");
                continue;
            }

            injector.Inject(propertyInfo, propertyHandler);
        }

        return true;
    }
}