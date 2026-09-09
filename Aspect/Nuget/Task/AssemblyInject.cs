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
        project.Save();
        return true;
    }

    private bool InjectAssemblies(string assemblyPath)
    {
        var assembly = Assembly.Read(assemblyPath);
        var assemblyHandler = new AssemblyHandler(assembly);
        var runtimeAssembly = System.Reflection.Assembly.LoadFrom(assemblyPath);
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
                if  (typeHandler is IFieldContainer fieldContainer)
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

        return dirty;
    }

    private bool ProcessAssembleInjector(AssemblyHandler assemblyHandler, System.Reflection.Assembly runtimeAssembly)
    {
        var injectors = runtimeAssembly.GetCustomAttributes(inherit: false)
                                       .Where(static item =>
                                           item is Attribute attr &&
                                           attr.GetType().GetInterfaces().Contains(typeof(IAssemblyInjector)) &&
                                           Attribute.GetCustomAttribute(attr.GetType(), typeof(AttributeUsageAttribute)) is AttributeUsageAttribute usage &&
                                           usage.ValidOn.HasFlag(AttributeTargets.Assembly))
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
                                 .Where(static item =>
                                     item is Attribute attr &&
                                     attr.GetType().GetInterfaces().Contains(typeof(ITypeInjector)) &&
                                     Attribute.GetCustomAttribute(attr.GetType(), typeof(AttributeUsageAttribute)) is AttributeUsageAttribute usage &&
                                     usage.ValidOn.HasFlag(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum))
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
                      .Where(static item =>
                          item is Attribute attr &&
                          attr.GetType().GetInterfaces().Contains(typeof(IMethodInjector)) &&
                          Attribute.GetCustomAttribute(attr.GetType(), typeof(AttributeUsageAttribute)) is AttributeUsageAttribute usage &&
                          usage.ValidOn.HasFlag(AttributeTargets.Method))
                      .Cast<IMethodInjector>()
                      .ToArray() is not {Length: > 0} injectors) return false;

        foreach (var injector in injectors)
        {
            injector.Inject(methodInfo, typeHandler.GetMethod(methodInfo.Name, methodInfo.GetParameters().GetITypes())!);
        }

        return false;
    }

    private bool ProcessFieldInjector(IFieldContainer typeHandler, Type runtimeType, FieldInfo fieldInfo)
    {
        if (fieldInfo.GetCustomAttributes(inherit: false)
                     .Where(static item =>
                         item is Attribute attr &&
                         attr.GetType().GetInterfaces().Contains(typeof(IFieldInjector)) &&
                         Attribute.GetCustomAttribute(attr.GetType(), typeof(AttributeUsageAttribute)) is AttributeUsageAttribute usage &&
                         usage.ValidOn.HasFlag(AttributeTargets.Field))
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

        return false;
    }

    private bool ProcessPropertyInjector(IPropertyContainer typeHandler, Type runtimeType, PropertyInfo propertyInfo)
    {
        if (propertyInfo.GetCustomAttributes(inherit: false)
                        .Where(static item =>
                            item is Attribute attr &&
                            attr.GetType().GetInterfaces().Contains(typeof(IPropertyInjector)) &&
                            Attribute.GetCustomAttribute(attr.GetType(), typeof(AttributeUsageAttribute)) is AttributeUsageAttribute usage &&
                            usage.ValidOn.HasFlag(AttributeTargets.Property))
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

        return false;
    }
}