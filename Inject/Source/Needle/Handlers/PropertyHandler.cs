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
internal class PropertyHandler(PropertyDefinition methodDef, TypeHandler declaringTypeHandler, MethodAttributes? accessorAttributes = null) : IPropertyHandler, IAttributeContainer
{
    /// <summary>
    /// Name of the property.
    /// </summary>
    public string Name => Source.Name;

    /// <summary>
    /// Full name of the property, which is its name qualified by the type which declares it.
    /// </summary>
    public string FullName => Source.FullName;

    /// <summary>
    /// The handler of the setter which was read or written, which is kept so that the accessor is read out of the
    /// metadata once rather than again at each ask.
    /// </summary>
    private MethodHandler? m_Setter;

    /// <summary>
    /// The handler of the getter which was read or written, which is kept so that the accessor is read out of the
    /// metadata once rather than again at each ask.
    /// </summary>
    private MethodHandler? m_Getter;

    /// <summary>
    /// The attributes which an accessor is created with, which are the attributes of the property when the decorator
    /// describes it and the attributes of an accessor which the property already holds otherwise.
    /// </summary>
    /// <remarks>
    /// The attributes are given at the creation of an accessor rather than written over it afterwards, because a body
    /// which reads or writes a field is emitted for the shape of the accessor which ends up holding it: <c>ldsfld</c>
    /// where <c>ldarg.0; ldfld</c> is emitted for an instance one. The attributes of a property always keep the special
    /// name which an accessor needs, see <see cref="PropertyFlagExtensions.ToMethodAttributes"/>.
    /// </remarks>
    private readonly MethodAttributes m_AccessorAttributes = accessorAttributes
                                                          ?? MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig;

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

    /// <summary>
    /// Give the getter of the property a body of a kind which can be written from the property alone, which the getter
    /// is added to the type for first when the property holds none.<para/>
    /// <see cref="DefaultPropertyBody.WithFieldOperation"/> writes the getter against the backing field
    /// <c>&lt;{Name}&gt;k__BackingField</c>, which is added to the type when it does not hold one, and is static
    /// exactly when the getter is.
    /// </summary>
    /// <param name="body">The kind of body which the getter is given.</param>
    /// <exception cref="ArgumentException">Thrown when the getter which the property holds is one which a body cannot be written for, or when the body has no getter form.</exception>
    public void SetGetter(DefaultPropertyBody body)
    {
        if (Source.GetMethod == null)
        {
            var methodDef = new MethodDefinition($"get_{Name}", m_AccessorAttributes, Source.PropertyType);
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
        VerifyHoldsBody(m_Getter.Source);
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
            throw new ArgumentException(string.Format(ErrorMessages.INDEXER_TAKES_NO_FIELD_OPERATION, Name));
        }

        // Default property body with field operation, which means the getter will return the value of a backing field,
        // and the setter will set the value of the backing field.
        // The backing field will be automatically created by the injector with the name "<{property_name}>k__BackingField",
        // and it belongs to the type rather than to an instance of it exactly when the accessor does.
        var isStatic = m_Getter.Source.IsStatic;
        var field = DeclaringTypeHandler.GetFieldInThis($"<{Name}>k__BackingField");
        if (field == null)
        {
            field = new FieldDefinition($"<{Name}>k__BackingField", FieldAttributes.Private | (isStatic ? FieldAttributes.Static : 0), Source.PropertyType);
            DeclaringTypeHandler.Source.Fields.Add((FieldDefinition) field);
        }

        var declaringType = DeclaringTypeHandler.Source;
        var fieldRef = field.ContainsGenericParameter
            // If the field contains generic parameter, we need to make a new FieldReference with the generic instance type of declaring type as its DeclaringType.
            // Related to issue: https://github.com/jbevain/cecil/issues/954
            ? new FieldReference(field.Name, field.FieldType, declaringType.MakeGenericInstanceType(declaringType.GenericParameters.Select(static p => (TypeReference) p).ToArray()))
            // Otherwise we can directly import the field definition as reference.
            : Source.Module.ImportReference(field);
        // A static accessor reaches the field through the type alone, where an instance one reaches it through `this`,
        // which is the slot before the parameters.
        m_Getter.Source.Body.Instructions.Clear();
        if (!isStatic) m_Getter.Source.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        m_Getter.Source.Body.Instructions.Add(Instruction.Create(isStatic ? OpCodes.Ldsfld : OpCodes.Ldfld, fieldRef));
        m_Getter.Source.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
    }

    /// <summary>
    /// Give the setter of the property a body of a kind which can be written from the property alone, which the setter
    /// is added to the type for first when the property holds none.<para/>
    /// <see cref="DefaultPropertyBody.WithFieldOperation"/> writes the setter against the backing field
    /// <c>&lt;{Name}&gt;k__BackingField</c>, which is added to the type when it does not hold one, and is static
    /// exactly when the setter is.
    /// </summary>
    /// <param name="body">The kind of body which the setter is given.</param>
    /// <exception cref="ArgumentException">Thrown when the setter which the property holds is one which a body cannot be written for, or when a body which operates on a field is asked for on an indexer.</exception>
    public void SetSetter(DefaultPropertyBody body)
    {
        if (Source.SetMethod == null)
        {
            var methodDef = new MethodDefinition($"set_{Name}", m_AccessorAttributes, Source.Module.TypeSystem.Void);
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
        VerifyHoldsBody(m_Setter.Source);
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
            throw new ArgumentException(string.Format(ErrorMessages.INDEXER_TAKES_NO_FIELD_OPERATION, Name));
        }

        // Default property body with field operation, which means the getter will return the value of a backing field,
        // and the getter will get the value from the backing field.
        // The backing field will be automatically created by the injector with the name "<{property_name}>k__BackingField",
        // and it belongs to the type rather than to an instance of it exactly when the accessor does.
        var isStatic = m_Setter.Source.IsStatic;
        var field = DeclaringTypeHandler.GetFieldInThis($"<{Name}>k__BackingField");
        if (field == null)
        {
            field = new FieldDefinition($"<{Name}>k__BackingField", FieldAttributes.Private | (isStatic ? FieldAttributes.Static : 0), Source.PropertyType);
            DeclaringTypeHandler.Source.Fields.Add((FieldDefinition) field);
        }

        var declaringType = DeclaringTypeHandler.Source;
        var fieldRef = field.ContainsGenericParameter
            // If the field contains generic parameter, we need to make a new FieldReference with the generic instance type of declaring type as its DeclaringType.
            // Related to issue: https://github.com/jbevain/cecil/issues/954
            ? new FieldReference(field.Name, field.FieldType, declaringType.MakeGenericInstanceType(declaringType.GenericParameters.Select(static p => (TypeReference) p).ToArray()))
            // Otherwise we can directly import the field definition as reference.
            : Source.Module.ImportReference(field);
        // A static setter holds the value in the slot zero, where an instance one holds `this` there and the value in the
        // slot after it.
        var setterBody = m_Setter.Source.Body.Instructions;
        setterBody.Clear();
        if (isStatic)
        {
            setterBody.Add(Instruction.Create(OpCodes.Ldarg_0));
            setterBody.Add(Instruction.Create(OpCodes.Stsfld, fieldRef));
        }
        else
        {
            setterBody.Add(Instruction.Create(OpCodes.Ldarg_0));
            setterBody.Add(Instruction.Create(OpCodes.Ldarg_1));
            setterBody.Add(Instruction.Create(OpCodes.Stfld, fieldRef));
        }

        setterBody.Add(Instruction.Create(OpCodes.Ret));
    }

    /// <summary>
    /// Copy the body of a member into the setter of the property, which the setter is added to the type for first when
    /// the property holds none.<para/>
    /// The member takes the value which is set, and takes the index before it when the property is an indexer, so a
    /// member of two parameters describes an indexer whose index type is the type of its first parameter.
    /// </summary>
    /// <param name="body">The member whose body the setter is given.</param>
    /// <exception cref="ArgumentException">Thrown when the member takes more than the value and the index, or when its last parameter is not the type of the property.</exception>
    public void SetSetter(MethodInfo body)
    {
        // Indexer has more than one parameter or the parameter type does not match the property type.
        var paramLength = body.GetParameters().Length;
        if (paramLength > 2
            || (paramLength == 1 && !TypeName.HasSameName(body.GetParameters()[0].ParameterType, Source.PropertyType))
            || (paramLength == 2 && !TypeName.HasSameName(body.GetParameters()[1].ParameterType, Source.PropertyType)))
        {
            throw new ArgumentException(string.Format(ErrorMessages.SETTER_MEMBER_DOES_NOT_MATCH, Name, Source.PropertyType.FullName));
        }

        if (Source.SetMethod == null)
        {
            var methodDef = new MethodDefinition($"set_{Name}", m_AccessorAttributes, Source.Module.TypeSystem.Void);
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
        VerifyHoldsBody(m_Setter.Source);
        m_Setter.SetBody(body);
    }

    /// <summary>
    /// Copy the body of a member into the getter of the property, which the getter is added to the type for first when
    /// the property holds none.<para/>
    /// The member takes the index when the property is an indexer, so a member of one parameter describes an indexer
    /// whose index type is the type of that parameter, and a member which takes none describes the getter of a property
    /// of the type itself.
    /// </summary>
    /// <param name="body">The member whose body the getter is given.</param>
    /// <exception cref="ArgumentException">Thrown when the member takes more than the index, or when what it hands back is not the type of the property.</exception>
    public void SetGetter(MethodInfo body)
    {
        // Indexer has more than one parameter or the parameter type does not match the property type.
        if (body.GetParameters().Length > 1 || !TypeName.HasSameName(body.ReturnType, Source.PropertyType))
            throw new ArgumentException(string.Format(ErrorMessages.GETTER_MEMBER_DOES_NOT_MATCH, Name, Source.PropertyType.FullName));

        if (Source.GetMethod == null)
        {
            var methodDef = new MethodDefinition($"get_{Name}", m_AccessorAttributes, Source.PropertyType);
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
        VerifyHoldsBody(m_Getter.Source);
        m_Getter.SetBody(body);
    }

    /// <summary>
    /// Refuse an accessor which holds no body, because none can be described for it.
    /// </summary>
    /// <param name="accessor">The accessor which a body is described for.</param>
    /// <exception cref="ArgumentException">Thrown when the accessor is abstract.</exception>
    private static void VerifyHoldsBody(MethodDefinition accessor)
    {
        if (accessor.IsAbstract) throw new ArgumentException(string.Format(ErrorMessages.ABSTRACT_ACCESSOR_HOLDS_NO_BODY, accessor.Name));
    }

    /// <inheritdoc/>
    public bool ContainsAttribute(IType attributeType) => Source.CustomAttributes.Any(attribute => TypeName.HasSameName(attribute.AttributeType, attributeType));

    /// <inheritdoc/>
    public void AddAttribute(IType attributeType, params object[] arguments)
    {
        var attributeDef = DeclaringTypeHandler.AssemblyHandler.GetCecilType(attributeType).Definition;
        var attribute = attributeDef.CreateCustomAttribute(DeclaringTypeHandler.AssemblyHandler.Assembly.Source.MainModule, arguments);
        Source.CustomAttributes.Add(attribute);
    }
}