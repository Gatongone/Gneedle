using System.Reflection;
using FieldAttributes = Mono.Cecil.FieldAttributes;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using ParameterAttributes = Mono.Cecil.ParameterAttributes;

namespace Gneedle.Inject;

/// <summary>
/// Represents a handler for a property of the metadata which is built, which reads its accessors and writes them.
/// </summary>
/// <param name="methodDef">The property definition which is handled.</param>
/// <param name="declaringTypeHandler">Handler of the type which declares the property.</param>
internal class PropertyHandler(PropertyDefinition methodDef, TypeHandler declaringTypeHandler) : IPropertyHandler, IAttributeContainer
{
    public string Name => Source.Name;
    public string FullName => Source.FullName;
    private           MethodHandler?     m_Setter;
    private           MethodHandler?     m_Getter;

    /// <summary>
    /// The property definition which is handled.
    /// </summary>
    internal readonly PropertyDefinition Source               = methodDef;

    /// <summary>
    /// Handler of the type which declares the property.
    /// </summary>
    internal readonly TypeHandler        DeclaringTypeHandler = declaringTypeHandler;
    ITypeHandler IPropertyHandler.DeclaringTypeHandler => DeclaringTypeHandler;

    /// <summary>
    /// Gets the getter method handler for this property. Returns null if the property does not have a getter.
    /// </summary>
    /// <returns>The getter method handler, or null if the property does not have a getter.</returns>
    public IMethodHandler? GetGetter()
    {
        return m_Getter ??= Source.GetMethod == null ? null : new MethodHandler(Source.GetMethod, DeclaringTypeHandler);
    }

    /// <summary>
    /// Gets the setter method handler for this property. Returns null if the property does not have a setter.
    /// </summary>
    /// <returns>The setter method handler, or null if the property does not have a setter.</returns>
    public IMethodHandler? GetSetter()
    {
        return m_Setter ??= Source.SetMethod == null ? null : new MethodHandler(Source.SetMethod, DeclaringTypeHandler);
    }

    public void SetGetter(DefaultPropertyBody body)
    {
        if (Source.GetMethod == null)
        {
            var methodDef = new MethodDefinition($"get_{Name}", MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig, Source.PropertyType);
            // If the property name is "Item", we treat it as an indexer setter,
            // and the parameter is the indexer parameter.
            if (Name == "Item")
            {
                // We treat the indexer parameter type as int.
                methodDef.Parameters.Add(new ParameterDefinition("index", ParameterAttributes.None, Source.Module.TypeSystem.Int32));
            }

            Source.GetMethod = methodDef;
            DeclaringTypeHandler.Source.Methods.Add(methodDef);
        }

        m_Getter = new MethodHandler(Source.GetMethod, DeclaringTypeHandler);
        // If the body is not default property body with field operation, we can directly set the body of the getter method using the provided delegate.
        if (body != DefaultPropertyBody.WithFieldOperation)
        {
            m_Getter.SetBody((DefaultMethodBody) body);
            return;
        }

        // Indexer is not supported for WithFieldOperation, because it doesn't have a default behaviour for getter method with parameters,
        // and the default property body with field operation only supports parameterless setter method.
        if (Source.GetMethod.Parameters.Count > 0)
        {
            throw new ArgumentException("The default property body with field operation does not support indexer.");
        }

        // Default property body with field operation, which means the getter will return the value of a backing field,
        // and the setter will set the value of the backing field.
        // The backing field will be automatically created by the injector with the name "<{property_name}>k__BackingField".
        var field = DeclaringTypeHandler.GetFieldInThis($"<{Name}>k__BackingField");
        if (field == null)
        {
            field = new FieldDefinition($"<{Name}>k__BackingField", FieldAttributes.Private, Source.PropertyType);
            DeclaringTypeHandler.Source.Fields.Add((FieldDefinition) field);
        }

        var declaringType = DeclaringTypeHandler.Source;
        var fieldRef = field.ContainsGenericParameter
            // If the field contains generic parameter, we need to make a new FieldReference with the generic instance type of declaring type as its DeclaringType.
            // Related to issue: https://github.com/jbevain/cecil/issues/954
            ? new FieldReference(field.Name, field.FieldType, declaringType.MakeGenericInstanceType(declaringType.GenericParameters.Select(static p => (TypeReference) p).ToArray()))
            // Otherwise we can directly import the field definition as reference.
            : Source.Module.ImportReference(field);
        m_Getter.Source.Body.Instructions.Clear();
        m_Getter.Source.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        m_Getter.Source.Body.Instructions.Add(Instruction.Create(OpCodes.Ldfld, fieldRef));
        m_Getter.Source.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
    }

    public void SetSetter(DefaultPropertyBody body)
    {
        if (Source.SetMethod == null)
        {
            var methodDef = new MethodDefinition($"set_{Name}", MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig, Source.Module.TypeSystem.Void);
            // If the property name is "Item", we treat it as an indexer setter,
            // and the first parameter is the indexer parameter,
            // and the second parameter is the value parameter.
            if (Name == "Item")
            {
                // We treat the indexer parameter type as int.
                methodDef.Parameters.Insert(0, new ParameterDefinition("index", ParameterAttributes.None, Source.Module.TypeSystem.Int32));
            }

            methodDef.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, Source.PropertyType));

            Source.SetMethod = methodDef;
            DeclaringTypeHandler.Source.Methods.Add(methodDef);
        }

        m_Setter = new MethodHandler(Source.SetMethod, DeclaringTypeHandler);
        // If the body is not default property body with field operation, we can directly set the body of the getter method using the provided delegate.
        if (body != DefaultPropertyBody.WithFieldOperation)
        {
            m_Setter.SetBody((DefaultMethodBody) body);
            return;
        }

        // Indexer is not supported for WithFieldOperation. A normal setter has a single
        // "value" parameter; only an indexer setter adds an extra "index" parameter.
        if (m_Setter.Source.Parameters.Count > 1)
        {
            throw new ArgumentException("The default property body with field operation does not support indexer.");
        }

        // Default property body with field operation, which means the getter will return the value of a backing field,
        // and the getter will get the value from the backing field.
        // The backing field will be automatically created by the injector with the name "<{property_name}>k__BackingField".
        var field = DeclaringTypeHandler.GetFieldInThis($"<{Name}>k__BackingField");
        if (field == null)
        {
            field = new FieldDefinition($"<{Name}>k__BackingField", FieldAttributes.Private, Source.PropertyType);
            DeclaringTypeHandler.Source.Fields.Add((FieldDefinition) field);
        }

        var declaringType = DeclaringTypeHandler.Source;
        var fieldRef = field.ContainsGenericParameter
            // If the field contains generic parameter, we need to make a new FieldReference with the generic instance type of declaring type as its DeclaringType.
            // Related to issue: https://github.com/jbevain/cecil/issues/954
            ? new FieldReference(field.Name, field.FieldType, declaringType.MakeGenericInstanceType(declaringType.GenericParameters.Select(static p => (TypeReference) p).ToArray()))
            // Otherwise we can directly import the field definition as reference.
            : Source.Module.ImportReference(field);
        m_Setter.Source.Body.Instructions.Clear();
        m_Setter.Source.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        m_Setter.Source.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
        m_Setter.Source.Body.Instructions.Add(Instruction.Create(OpCodes.Stfld, fieldRef));
        m_Setter.Source.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
    }

    public void SetSetter(MethodInfo body)
    {
        // Indexer has more than one parameter or the parameter type does not match the property type.
        var paramLength = body.GetParameters().Length;
        if (paramLength > 2
            || (paramLength == 1 && !TypeName.HasSameName(body.GetParameters()[0].ParameterType, Source.PropertyType))
            || (paramLength == 2 && !TypeName.HasSameName(body.GetParameters()[1].ParameterType, Source.PropertyType)))
        {
            throw new ArgumentException($"The provided delegate must have exactly one parameter of type {Source.PropertyType.FullName}.");
        }

        if (Source.SetMethod == null)
        {
            var methodDef = new MethodDefinition($"set_{Name}", MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig, Source.Module.TypeSystem.Void);
            if (paramLength == 2)
            {
                // If the delegate has two parameters, we treat it as an indexer setter,
                // and the first parameter is the indexer parameter,
                // and the second parameter is the value parameter.
                methodDef.Parameters.Add(new ParameterDefinition("index", ParameterAttributes.None, DeclaringTypeHandler.AssemblyHandler.GetCecilType(body.GetParameters()[0].ParameterType).Reference));
            }

            methodDef.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, Source.PropertyType));
            Source.SetMethod = methodDef;
            DeclaringTypeHandler.Source.Methods.Add(methodDef);
        }

        m_Setter = new MethodHandler(Source.SetMethod, DeclaringTypeHandler);
        m_Setter.SetBody(body);
    }

    public void SetGetter(MethodInfo body)
    {
        // Indexer has more than one parameter or the parameter type does not match the property type.
        if (body.GetParameters().Length > 1 || !TypeName.HasSameName(body.ReturnType, Source.PropertyType))
            throw new ArgumentException($"The provided delegate must have no parameters and return a value of type {Source.PropertyType.FullName}.");

        if (Source.GetMethod == null)
        {
            var methodDef = new MethodDefinition($"get_{Name}", MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig, Source.PropertyType);
            // If the delegate has one parameter, we treat it as an indexer getter,
            // and the parameter is the indexer parameter.
            if (body.GetParameters().Length == 1)
            {
                methodDef.Parameters.Add(new ParameterDefinition("index", ParameterAttributes.None, DeclaringTypeHandler.AssemblyHandler.GetCecilType(body.GetParameters()[0].ParameterType).Reference));
            }

            Source.GetMethod = methodDef;
            DeclaringTypeHandler.Source.Methods.Add(methodDef);
        }

        m_Getter = new MethodHandler(Source.GetMethod, DeclaringTypeHandler);
        m_Getter.SetBody(body);
    }

    /// <inheritdoc/>
    public bool ContainsAttribute(IType attributeType) => Source.CustomAttributes.Any(attribute => TypeName.HasSameName(attribute.AttributeType, attributeType));

    /// <inheritdoc/>
    public void AddAttribute(IType attributeType, params object[] arguments)
    {
        var typeDef = DeclaringTypeHandler.AssemblyHandler.GetCecilType(attributeType).Definition;
        var attribute = typeDef.CreateCustomAttribute(DeclaringTypeHandler.AssemblyHandler.Assembly.Source.MainModule, arguments);
        typeDef.CustomAttributes.Add(attribute);
    }
}