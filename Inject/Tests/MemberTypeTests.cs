using Mono.Cecil;

namespace Gneedle.Inject.Test;

using static TestFixtures;

/// <summary>
/// The types which a handler reads off the definition which it handles, which is the inverse of what the decorator
/// writes into that definition: the types of the arguments and the type of the value which a method hands back, the
/// type which a field holds, and the type which a property reads and writes. They are read out of a definition of the
/// assembly being woven, which holds no runtime type to describe them with, so they are described by the names which
/// the metadata writes for them.
/// </summary>
[TestFixture]
public class MemberTypeTests
{
    /// <summary>
    /// Declare a class of the module of a host, which is a type which the assembly being woven declares, and answer with
    /// its definition.
    /// </summary>
    /// <param name="module">The module which declares the host.</param>
    /// <param name="typeName">The name of the class to declare.</param>
    /// <returns>The definition of the class which was declared.</returns>
    private static TypeDefinition DeclareType(ModuleDefinition module, string typeName)
    {
        var declared = new TypeDefinition(NS, typeName, TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
        module.Types.Add(declared);
        return declared;
    }

    /// <summary>
    /// The description of a type which <see cref="DeclareType"/> declared, which is the name of it as the metadata
    /// writes it.
    /// </summary>
    /// <param name="typeName">The name of the class.</param>
    /// <returns>The description of the class.</returns>
    private static ReferencedType Declared(string typeName) => new($"{NS}.{typeName}");

    #region Method

    [Test]
    public void The_Argument_Types_And_The_Return_Type_Are_The_Ones_Which_The_Definition_Declares()
    {
        var (_, host, _) = NewHost("MemberTypeMethodAssembly");

        var method = host.AddMethod("Run", MethodFlags.Public | MethodFlags.Static)
                         .WithParameter("text", typeof(string))
                         .WithParameter("count", typeof(int))
                         .WithParameter("values", typeof(int[]))
                         .WithReturnType(typeof(int))
                         .GetHandler();

        Assert.Multiple(() =>
        {
            Assert.That(method.ArgumentTypes.Select(type => type.GetTypeName()), Is.EqualTo(new[]
            {
                typeof(string).ToIType().GetTypeName(),
                typeof(int).ToIType().GetTypeName(),
                typeof(int[]).ToIType().GetTypeName()
            }));
            Assert.That(method.ReturnType.GetTypeName(), Is.EqualTo(typeof(int).ToIType().GetTypeName()));
        });
    }

    [Test]
    public void A_Method_Which_Takes_Nothing_And_Hands_Nothing_Back_Is_Described_By_No_Argument_And_By_The_Type_Of_Nothing()
    {
        var (_, host, _) = NewHost("MemberTypeOfNothingAssembly");

        var method = host.AddMethod("Run", MethodFlags.Public).GetHandler();

        Assert.Multiple(() =>
        {
            Assert.That(method.ArgumentTypes, Is.Empty);
            Assert.That(method.ReturnType.GetTypeName(), Is.EqualTo(typeof(void).ToIType().GetTypeName()));
        });
    }

    [Test]
    public void A_Type_Which_The_Assembly_Being_Woven_Declares_Is_Described_By_The_Name_Of_It()
    {
        // A type of the assembly being woven holds no System.Type to describe it with, because that assembly is read as
        // metadata: the name which the reference of the metadata writes is what describes it instead, and that name is
        // the one which a description of the type of that name is read as well.
        var (_, host, module) = NewHost("MemberTypeDeclaredTypeAssembly");
        DeclareType(module, "Holder");

        var method = host.AddMethod("Run", MethodFlags.Public).WithParameter("holder", Declared("Holder")).GetHandler();

        Assert.Multiple(() =>
        {
            Assert.That(method.ArgumentTypes, Has.Length.EqualTo(1));
            Assert.That(method.ArgumentTypes[0], Is.TypeOf<ReferencedType>());
            Assert.That(method.ArgumentTypes[0].GetTypeName(), Is.EqualTo(Declared("Holder").GetTypeName()));
        });
    }

    [Test]
    public void A_Generic_Parameter_Of_The_Method_Is_Described_By_The_Name_Of_It()
    {
        // A parameter of the method itself stands for whatever instantiates it rather than for a type of an assembly,
        // so it is described by the name of that parameter, which is the shape a caller names it with as well.
        var (_, host, _) = NewHost("MemberTypeGenericParameterAssembly");

        var method = host.AddMethod("Echo", MethodFlags.Public)
                         .WithGenericParameter("T")
                         .WithParameter("value", new GenericParameterType("T"))
                         .WithReturnType(new GenericParameterType("T"))
                         .GetHandler();

        Assert.Multiple(() =>
        {
            Assert.That(method.ArgumentTypes, Has.Length.EqualTo(1));
            Assert.That(method.ArgumentTypes[0], Is.TypeOf<GenericParameterType>());
            Assert.That(method.ArgumentTypes[0].GetTypeName(), Is.EqualTo(new GenericParameterType("T").GetTypeName()));
            Assert.That(method.ReturnType, Is.TypeOf<GenericParameterType>());
            Assert.That(method.ReturnType.GetTypeName(), Is.EqualTo(new GenericParameterType("T").GetTypeName()));
        });
    }

    [Test]
    public void The_Declared_Generic_Parameters_Are_The_Ones_Which_The_Definition_Declares()
    {
        var (_, host, _) = NewHost("MemberTypeDeclaredGenericParametersAssembly");

        var method = host.AddMethod("Run", MethodFlags.Public | MethodFlags.Static)
                         .WithGenericParameter("T", Constraint.Class)
                         .WithGenericParameter("U", Constraint.FromType<IDisposable>())
                         .GetHandler();

        Assert.Multiple(() =>
        {
            Assert.That(method.GenericParameters.Select(parameter => parameter.TypeName), Is.EqualTo(new[] {"T", "U"}));

            // The kind which a constraint names by an attribute is described by the shape of this tree which stands for
            // it, and a constraint which names a type is described by that type.
            Assert.That(method.GenericParameters[0].Constraints.Select(constraint => constraint.Name), Is.EqualTo(new[] {Constraint.Class.Name}));
            Assert.That(method.GenericParameters[1].Constraints.Select(constraint => constraint.Name), Is.EqualTo(new[] {typeof(IDisposable).FullName}));
        });
    }

    [Test]
    public void A_Method_Which_Declares_No_Generic_Parameter_Is_Answered_With_None()
    {
        var (_, host, _) = NewHost("MemberTypeNoGenericParameterAssembly");

        Assert.That(host.AddMethod("Run", MethodFlags.Public).GetHandler().GenericParameters, Is.Empty);
    }

    [Test]
    public void The_Generic_Parameters_Which_A_Handler_Reads_Can_Be_Written_Into_Another_Method()
    {
        // The kinds which a constraint names by an attribute are read as the shapes of this tree which stand for them,
        // and a shape writes the kind again, so a parameter which is read and written again declares what it declared:
        // the two are compared on the metadata, which is what the reading and the writing are two directions of.
        var (_, host, _) = NewHost("MemberTypeGenericParameterRoundTripAssembly");

        var first = host.AddMethod("First", MethodFlags.Public)
                        .WithGenericParameter("T", Constraint.Class, Constraint.New, Constraint.FromType<IDisposable>())
                        .GetHandler();

        var second = host.AddMethod("Second", MethodFlags.Public)
                         .WithGenericParameter("T", [.. first.GenericParameters[0].Constraints])
                         .GetHandler();

        var read = ((MethodHandler) first).Source.GenericParameters[0];
        var written = ((MethodHandler) second).Source.GenericParameters[0];

        Assert.Multiple(() =>
        {
            Assert.That(written.Attributes, Is.EqualTo(read.Attributes));
            Assert.That(written.Constraints.Select(constraint => constraint.ConstraintType.FullName),
                Is.EqualTo(read.Constraints.Select(constraint => constraint.ConstraintType.FullName)));
        });
    }

    [Test]
    public void The_Types_Which_A_Handler_Reads_Are_The_Ones_Which_The_Query_Of_It_Is_Asked_With()
    {
        // The two readings of a type are one type to the names of the tree, so the signature which a handler answered
        // with is the very signature which the query of the type is asked for a method by.
        var (_, host, _) = NewHost("MemberTypeQueryAssembly");

        var method = host.AddMethod("Run", MethodFlags.Public | MethodFlags.Static)
                         .WithParameter("text", typeof(string))
                         .WithParameter("count", typeof(int))
                         .WithReturnType(typeof(int))
                         .GetHandler();

        var asked = host.GetMethod("Run", method.ArgumentTypes);

        Assert.That(asked, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(asked!.ArgumentTypes.Select(type => type.GetTypeName()), Is.EqualTo(method.ArgumentTypes.Select(type => type.GetTypeName())));
            Assert.That(asked.ReturnType.GetTypeName(), Is.EqualTo(method.ReturnType.GetTypeName()));
        });
    }

    [Test]
    public void The_Types_Which_A_Handler_Reads_Can_Be_Written_Into_Another_Member()
    {
        // A name is what the writing of a member resolves a type by as well, so what a handler read out of one member
        // is a description which another member can be described with.
        var (_, host, module) = NewHost("MemberTypeWriteBackAssembly");
        DeclareType(module, "Holder");

        var first = host.AddMethod("First", MethodFlags.Public)
                        .WithParameter("holder", Declared("Holder"))
                        .WithReturnType(typeof(int))
                        .GetHandler();

        var second = host.AddMethod("Second", MethodFlags.Public)
                         .WithParameter("holder", first.ArgumentTypes[0])
                         .WithReturnType(first.ReturnType)
                         .GetHandler();

        Assert.Multiple(() =>
        {
            Assert.That(second.ArgumentTypes[0].GetTypeName(), Is.EqualTo(first.ArgumentTypes[0].GetTypeName()));
            Assert.That(((MethodHandler) second).Source.Parameters[0].ParameterType.FullName, Is.EqualTo($"{NS}.Holder"));
            Assert.That(((MethodHandler) second).Source.ReturnType.FullName, Is.EqualTo(typeof(int).FullName));
        });
    }

    #endregion

    #region Field

    [Test]
    public void The_Field_Type_Is_The_One_Which_The_Definition_Declares()
    {
        var (_, host, module) = NewHost("MemberTypeFieldAssembly");
        DeclareType(module, "Holder");

        // The field which the decorator wrote and the one which was read out of the metadata are both read back, so
        // that the type of a field is read the same way whichever of the two wrote it.
        var written = host.AddField("Value", FieldFlags.Public)
                          .WithType(Declared("Holder"))
                          .GetHandler();
        host.Source.Fields.Add(new FieldDefinition("Counter", FieldAttributes.Public | FieldAttributes.Static, module.TypeSystem.Int32));

        Assert.Multiple(() =>
        {
            Assert.That(written.FieldType, Is.TypeOf<ReferencedType>());
            Assert.That(written.FieldType.GetTypeName(), Is.EqualTo(Declared("Holder").GetTypeName()));
            Assert.That(host.GetField("Counter")!.FieldType.GetTypeName(), Is.EqualTo(typeof(int).ToIType().GetTypeName()));
        });
    }

    #endregion

    #region Property

    [Test]
    public void The_Property_Type_Is_The_One_Which_The_Definition_Declares()
    {
        var (_, host, module) = NewHost("MemberTypePropertyAssembly");
        var declared = DeclareType(module, "Holder");

        var written = host.AddProperty("Count", PropertyFlags.Public)
                          .WithType(typeof(int))
                          .WithGetter(() => 0)
                          .GetHandler();

        // A property which holds no accessor holds its type all the same, which is the one this reads: the accessors
        // are what the flags of a property are read off, and not what the type of it is read off.
        host.Source.Properties.Add(new PropertyDefinition("Value", PropertyAttributes.None, declared));

        Assert.Multiple(() =>
        {
            Assert.That(written.PropertyType.GetTypeName(), Is.EqualTo(typeof(int).ToIType().GetTypeName()));
            Assert.That(host.GetProperty("Value")!.PropertyType, Is.TypeOf<ReferencedType>());
            Assert.That(host.GetProperty("Value")!.PropertyType.GetTypeName(), Is.EqualTo(Declared("Holder").GetTypeName()));
        });
    }

    #endregion
}
