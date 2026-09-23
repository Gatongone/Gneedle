namespace Gneedle.Inject;

/// <summary>
/// Represents a handler for method definitions, providing functionalities to manipulate method bodies and translate instructions for code injection purposes.
/// </summary>
internal sealed partial class MethodHandler : IMethodHandler
{
    /// <summary>
    /// The source method definition to inject code into. It is the method definition we want to manipulate and inject code into.
    /// </summary>
    internal readonly MethodDefinition Source;

    /// <summary>
    /// The type handler of the type that declares the source method definition.
    /// It is used to get the context of the source method definition when translating instructions and parsing members in target method definition.
    /// </summary>
    internal readonly TypeHandler DeclaringTypeHandler;

    /// <summary>
    /// The generated method which holds the body which the source method is woven around, or null when the source
    /// method is not woven around.<para/>
    /// The method itself is held rather than looked up on the declaring type by the name of it, because the template is
    /// parsed before the declaring type declares the generated method: what a template proceeds into is the body which
    /// was taken over, and that body belongs to this member whether or not the method which holds it is on the type yet.
    /// </summary>
    private MethodDefinition? m_ProceedMethod;

    /// <summary>
    /// The instance which the delegate of the template was made from, whose fields hold the variables which the
    /// template captured, or null when the template was given as a method rather than as the delegate of it.<para/>
    /// A lambda which captures a variable is an instance method of the type which the compiler wrote to hold it, and it
    /// reads what it captured off that instance. The values themselves are held by the delegate, which the weaving is
    /// given while the injector runs, so what the template captured is written where the template read it.
    /// </summary>
    private object? m_TemplateClosure;

    /// <summary>
    /// The types and the members which the compiler wrote for the bodies of the template's own, which the carrying has
    /// moved onto the type being woven, or null while no template is being parsed.<para/>
    /// The copy of a type the compiler wrote keeps the name of the original, so the name alone no longer tells a type
    /// which the weaving holds the instructions of from one which it does not: what was carried is held here, and the
    /// refusals ask it.
    /// </summary>
    private CarriedBodies? m_Carried;

    /// <summary>
    /// The body of the compiler's own which is being woven, or null while the body of the template itself is.<para/>
    /// A body of the compiler's own is woven in its own right rather than as instructions of the template, so the
    /// questions the parsing asks of a body - which argument a slot names, which local, which receiver - are asked of
    /// the copy rather than of the member being woven.
    /// </summary>
    private CarriedBodies.Body? m_CarriedBody;

    /// <inheritdoc/>
    public MethodFlags Flags => Source.ToMethodFlags();

    /// <summary>
    /// Gets the name of the source method definition. It is used for debugging and logging purposes to identify the method being manipulated.
    /// </summary>
    public string Name => Source.Name;

    /// <inheritdoc/>
    ITypeHandler IMethodHandler.DeclaringTypeHandler => DeclaringTypeHandler;

    /// <inheritdoc/>
    public bool ContainsAttribute(IType attributeType) => Source.CustomAttributes.Any(attribute => TypeName.HasSameName(attribute.AttributeType, attributeType));

    /// <inheritdoc/>
    public void AddAttribute(IType attributeType, params object[] arguments)
    {
        var attributeDef = DeclaringTypeHandler.AssemblyHandler.GetCecilType(attributeType).Definition;
        var attribute = attributeDef.CreateCustomAttribute(DeclaringTypeHandler.AssemblyHandler.Assembly.Source.MainModule, arguments);
        Source.CustomAttributes.Add(attribute);
    }

    /// <summary>
    /// Initialize a new instance of MethodHandler with the source method definition and its declaring type handler.
    /// The source method definition is the method definition we want to inject code into, and the declaring type handler is the type handler of the type that declares the source method definition.
    /// </summary>
    /// <param name="methodDef"></param>
    /// <param name="declaringTypeHandler"></param>
    internal MethodHandler(MethodDefinition methodDef, TypeHandler declaringTypeHandler) => (Source, DeclaringTypeHandler) = (methodDef, declaringTypeHandler);

    /// <summary>
    /// The method which a body is woven into, which is what the walks of that body read it against.
    /// </summary>
    private ParseContext Context => new(Source);

    /// <summary>
    /// Get the string representation of the method, which is the declaration of it and the IL of the body which it
    /// holds.
    /// </summary>
    /// <returns>The declaration of the method, and the IL of its body.</returns>
    public override string ToString() => IlPrinter.Print(Source);
}