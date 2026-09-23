using System.Reflection;
using OpCodes = Mono.Cecil.Cil.OpCodes;

namespace Gneedle.Inject.Test;

using static TestFixtures;

[TestFixture]
public class DecoratorTests
{
    /// <summary>
    /// Template bodies live in the test assembly so Cecil can resolve them from disk.
    /// </summary>
    public static class Templates
    {
        public static int Add(int left, int right) => left + right;
    }

    private static MethodInfo AddTemplate() => typeof(Templates).GetMethod(nameof(Templates.Add))!;

    private static TypeHandler NewClass()
    {
        var handler = (AssemblyHandler) Assembly.Create("DecoratorTestsAssembly").Handler;
        return (TypeHandler) handler.AddClass("Host", NS, ClassFlags.Public).GetHandler();
    }

    #region ClassDecorator

    [Test]
    public void AddClass_WithGenericParameter_Creates_Generic_Class()
    {
        var asm = Assembly.Create("DecoratorAssembly");
        var handler = (AssemblyHandler) asm.Handler;

        var classHandler = (ClassHandler) handler.AddClass("GenericClass", NS, ClassFlags.Public)
                                                 .WithGenericParameter("T")
                                                 .GetHandler();

        Assert.That(classHandler.Source.GenericParameters.Count, Is.EqualTo(1));
        Assert.That(classHandler.Source.GenericParameters[0].Name, Is.EqualTo("T"));
    }

    [Test]
    public void AddClass_WithBaseType_Sets_Correct_BaseType()
    {
        var asm = Assembly.Create("DecoratorAssembly");
        var handler = (AssemblyHandler) asm.Handler;

        var classHandler = (ClassHandler) handler.AddClass("DerivedClass", NS, ClassFlags.Public)
                                                 .WithBaseType(typeof(TestBaseClass))
                                                 .GetHandler();

        Assert.That(classHandler.Source.BaseType.FullName, Is.EqualTo(typeof(TestBaseClass).FullName));
    }

    [Test]
    public void AddClass_WithInterface_Adds_Interface_Implementation()
    {
        var asm = Assembly.Create("DecoratorAssembly");
        var handler = (AssemblyHandler) asm.Handler;

        var classHandler = (ClassHandler) handler.AddClass("ImplClass", NS, ClassFlags.Public)
                                                 .WithInterface(typeof(ITestInterface))
                                                 .GetHandler();

        Assert.That(classHandler.Source.Interfaces.Count, Is.GreaterThan(0));
        Assert.That(classHandler.Source.Interfaces.Any(i => i.InterfaceType.FullName == typeof(ITestInterface).FullName), Is.True);
    }

    [Test]
    public void AddClass_WithGenericParameter_And_BaseType_Creates_Generic_Derived_Class()
    {
        var asm = Assembly.Create("DecoratorAssembly");
        var handler = (AssemblyHandler) asm.Handler;

        var classHandler = (ClassHandler) handler.AddClass("GenericDerived", NS, ClassFlags.Public)
                                                 .WithGenericParameter("T")
                                                 .WithBaseType(typeof(TestBaseClass))
                                                 .GetHandler();
        Assert.Multiple(() =>
        {
            Assert.That(classHandler.Source.GenericParameters.Count, Is.EqualTo(1));
            Assert.That(classHandler.Source.BaseType.FullName, Is.EqualTo(typeof(TestBaseClass).FullName));
        });
    }

    [Test]
    public void AddClass_WithGenericParameter_And_Interface_Creates_Generic_Class_With_Interface()
    {
        var asm = Assembly.Create("DecoratorAssembly");
        var handler = (AssemblyHandler) asm.Handler;

        var classHandler = (ClassHandler) handler.AddClass("GenericImpl", NS, ClassFlags.Public)
                                                 .WithGenericParameter("T")
                                                 .WithInterface(typeof(ITestInterface))
                                                 .GetHandler();
        Assert.Multiple(() =>
        {
            Assert.That(classHandler.Source.GenericParameters.Count, Is.EqualTo(1));
            Assert.That(classHandler.Source.Interfaces.Any(i => i.InterfaceType.FullName == typeof(ITestInterface).FullName), Is.True);
        });
    }

    [Test]
    public void AddClass_WithBaseType_And_Interface_Creates_Derived_Class_With_Interface()
    {
        var asm = Assembly.Create("DecoratorAssembly");
        var handler = (AssemblyHandler) asm.Handler;

        var classHandler = (ClassHandler) handler.AddClass("DerivedImpl", NS, ClassFlags.Public)
                                                 .WithBaseType(typeof(TestBaseClass))
                                                 .WithInterface(typeof(ITestInterface))
                                                 .GetHandler();
        Assert.Multiple(() =>
        {
            Assert.That(classHandler.Source.BaseType.FullName, Is.EqualTo(typeof(TestBaseClass).FullName));
            Assert.That(classHandler.Source.Interfaces.Any(i => i.InterfaceType.FullName == typeof(ITestInterface).FullName), Is.True);
        });
    }

    [Test]
    public void AddClass_WithAllDecorators_Creates_Generic_Derived_Class_With_Interface()
    {
        var asm = Assembly.Create("DecoratorAssembly");
        var handler = (AssemblyHandler) asm.Handler;

        var classHandler = (ClassHandler) handler.AddClass("FullyDecoratedClass", NS, ClassFlags.Public)
                                                 .WithGenericParameter("T")
                                                 .WithGenericParameter("U")
                                                 .WithBaseType(typeof(TestBaseClass))
                                                 .WithInterface(typeof(ITestInterface))
                                                 .GetHandler();
        Assert.Multiple(() =>
        {
            Assert.That(classHandler.Source.GenericParameters.Count, Is.EqualTo(2));
            Assert.That(classHandler.Source.BaseType.FullName, Is.EqualTo(typeof(TestBaseClass).FullName));
            Assert.That(classHandler.Source.Interfaces.Any(i => i.InterfaceType.FullName == typeof(ITestInterface).FullName), Is.True);
        });
    }

    #endregion

    #region MethodDecorator

    [Test]
    public void MethodDecorator_Chain_Builds_Method()
    {
        var host = NewClass();
        var method = host.AddMethod("Compute", MethodFlags.Public | MethodFlags.Static)
                         .WithParameter("count", typeof(int))
                         .WithParameter("label", typeof(string))
                         .WithReturnType(typeof(int))
                         .GetHandler();

        Assert.That(method, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(method.Name, Is.EqualTo("Compute"));
            Assert.That(((MethodHandler) method).Source.IsStatic, Is.True);
            Assert.That(((MethodHandler) method).Source.ReturnType.FullName, Is.EqualTo(typeof(int).FullName));
            Assert.That(((MethodHandler) method).Source.Parameters.Count, Is.EqualTo(2));
        });
    }

    [Test]
    public void MethodDecorator_WithParameter_Names_The_Parameters_Of_The_Produced_Method()
    {
        var host = NewClass();
        var method = host.AddMethod("Compute", MethodFlags.Public | MethodFlags.Static)
                         .WithParameter(new Parameter("left", typeof(int).ToGneedleType()))
                         .WithParameter("right", typeof(int))
                         .WithReturnType(typeof(int))
                         .GetHandler();

        var parameters = ((MethodHandler) method).Source.Parameters;
        Assert.Multiple(() =>
        {
            Assert.That(parameters[0].Name, Is.EqualTo("left"));
            Assert.That(parameters[1].Name, Is.EqualTo("right"));
            Assert.That(parameters[0].ParameterType.FullName, Is.EqualTo(typeof(int).FullName));
        });
    }

    [Test]
    public void MethodDecorator_WithParameter_Without_A_Name_Leaves_The_Parameter_Unnamed()
    {
        var host = NewClass();
        var method = host.AddMethod("Compute", MethodFlags.Public | MethodFlags.Static)
                         .WithParameter(new Parameter(typeof(int).ToGneedleType()))
                         .GetHandler();

        Assert.That(((MethodHandler) method).Source.Parameters[0].Name, Is.Empty);
    }

    [Test]
    public void MethodDecorator_WithBody_Emits_Throw()
    {
        var host = NewClass();
        var method = host.AddMethod("Do", MethodFlags.Public)
                         .WithBody(DefaultMethodBody.ThrowException)
                         .GetHandler();

        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();
        Assert.That(ins.Any(i => i.OpCode == OpCodes.Throw), Is.True);
    }

    [Test]
    public void MethodDecorator_WithBody_From_MethodInfo_Copies_The_Body()
    {
        var host = NewClass();
        var method = host.AddMethod("Compute", MethodFlags.Public | MethodFlags.Static)
                         .WithParameter("left", typeof(int))
                         .WithParameter("right", typeof(int))
                         .WithReturnType(typeof(int))
                         .WithBody(AddTemplate())
                         .GetHandler();

        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();
        Assert.That(ins.Any(i => i.OpCode == OpCodes.Add), Is.True);
    }

    [Test]
    public void MethodDecorator_WithBody_From_Delegate_Copies_The_Body()
    {
        var template = Templates.Add;

        var host = NewClass();
        var method = host.AddMethod("Compute", MethodFlags.Public | MethodFlags.Static)
                         .WithParameter("left", typeof(int))
                         .WithParameter("right", typeof(int))
                         .WithReturnType(typeof(int))
                         .WithBody(template)
                         .GetHandler();

        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();
        Assert.That(ins.Any(i => i.OpCode == OpCodes.Add), Is.True);
    }

    [Test]
    public void MethodDecorator_WithBody_Of_A_Lambda_Which_Captured_A_Variable_Writes_The_Value()
    {
        // The delegate is what the chain is given and what it holds until the method is appended, so what the lambda
        // captured is read while the chain ends and written into the body which the method copies.
        var assembly = Assembly.Create("MethodDecoratorCaptureAssembly");
        var host = AddAHost(assembly);
        var captured = 41;

        var method = host.AddMethod("Compute", MethodFlags.Public | MethodFlags.Static)
                         .WithReturnType(typeof(int))
                         .WithBody(() => captured)
                         .GetHandler();

        var body = ((MethodHandler) method).Source.Body;
        Assert.Multiple(() =>
        {
            Assert.That(body.Instructions.Any(instruction => instruction.OpCode == OpCodes.Ldarg_0), Is.False);
            Assert.That(body.Instructions.Any(instruction => instruction.OpCode == OpCodes.Ldc_I4 && instruction.Operand is 41), Is.True);
        });
        var type = assembly.Load().GetType($"{NS}.Host")!;
        Assert.That(type.GetMethod("Compute")!.Invoke(null, null), Is.EqualTo(41));
    }

    [Test]
    public void PropertyDecorator_WithGetter_Of_A_Lambda_Which_Captured_A_Variable_Writes_The_Value()
    {
        // A getter is described by a delegate as well, and what the lambda captured is written into the accessor which
        // the chain appends rather than read off the instance which the delegate held.
        var host = NewClass();
        var captured = 41;

        var property = host.AddProperty("Value", PropertyFlags.Public)
                           .WithType(typeof(int))
                           .WithGetter(() => captured)
                           .GetHandler();

        var body = ((MethodHandler) property.GetGetter()!).Source.Body;
        Assert.Multiple(() =>
        {
            Assert.That(body.Instructions.Any(instruction => instruction.OpCode == OpCodes.Ldarg_0), Is.False);
            Assert.That(body.Instructions.Any(instruction => instruction.OpCode == OpCodes.Ldc_I4 && instruction.Operand is 41), Is.True);
        });
    }

    [Test]
    public void PropertyDecorator_WithSetter_Of_A_Lambda_Which_Captured_A_Variable_Writes_The_Value()
    {
        var host = NewClass();
        var captured = 41;

        var property = host.AddProperty("Value", PropertyFlags.Public)
                           .WithType(typeof(int))
                           .WithSetter((int value) => Console.WriteLine(value + captured))
                           .GetHandler();

        // The setter reads the value which it was given off its own receiver, which is not what the template loaded, so
        // nothing of the body reads the instance the delegate held.
        var body = ((MethodHandler) property.GetSetter()!).Source.Body;
        Assert.Multiple(() =>
        {
            Assert.That(body.Instructions.Any(instruction => instruction.OpCode == OpCodes.Ldarg_0), Is.False);
            Assert.That(body.Instructions.Any(instruction => instruction.OpCode == OpCodes.Ldc_I4 && instruction.Operand is 41), Is.True);
        });
    }

    #endregion

    #region FieldDecorator

    [Test]
    public void FieldDecorator_Chain_Builds_Field()
    {
        var host = NewClass();
        var field = host.AddField("Counter", FieldFlags.Public | FieldFlags.Static)
                        .WithType(typeof(int))
                        .GetHandler();

        Assert.That(field, Is.Not.Null);
        Assert.That(field.Name, Is.EqualTo("Counter"));
        var source = ((FieldHandler) field).Source;
        Assert.Multiple(() =>
        {
            Assert.That(source.IsStatic, Is.True);
            Assert.That(source.FieldType.FullName, Is.EqualTo(typeof(int).FullName));
            Assert.That(host.Source.Fields.Contains(source), Is.True);
        });
    }

    #endregion

    #region PropertyDecorator

    [Test]
    public void PropertyDecorator_Chain_Builds_Property()
    {
        var host = NewClass();
        var property = host.AddProperty("Value", PropertyFlags.Public)
                           .WithType(typeof(int))
                           .WithGetter(DefaultPropertyBody.WithFieldOperation)
                           .WithSetter(DefaultPropertyBody.WithFieldOperation)
                           .GetHandler();

        Assert.That(property, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(property.Name, Is.EqualTo("Value"));
            Assert.That(property.GetGetter(), Is.Not.Null);
            Assert.That(property.GetSetter(), Is.Not.Null);
            Assert.That(host.Source.Properties.Any(p => p.Name == "Value"), Is.True);
        });
    }

    [Test]
    public void PropertyDecorator_WithFieldOperation_Creates_Backing_Field()
    {
        var host = NewClass();
        host.AddProperty("Value", PropertyFlags.Public)
            .WithType(typeof(int))
            .WithGetter(DefaultPropertyBody.WithFieldOperation)
            .WithSetter(DefaultPropertyBody.WithFieldOperation)
            .GetHandler();

        // Backing field should be created: <Value>k__BackingField
        var backingField = host.Source.Fields.FirstOrDefault(f => f.Name == "<Value>k__BackingField");
        Assert.That(backingField, Is.Not.Null);
        Assert.That(backingField!.IsPrivate, Is.True);
    }

    #endregion

    #region The parts which a chain is made of

    /// <summary>
    /// The names of the parts which a level of a chain asks for by itself, which is what that level declares.
    /// </summary>
    private static string[] DeclaredParts(Type level)
        =>
        [
            .. level.GetMethods()
                    .Where(method => method.DeclaringType == level)
                    .Select(method => method.Name)
                    .Distinct()
                    .OrderBy(name => name)
        ];

    /// <summary>
    /// The names of the parts which a level of a chain reaches, which is what it declares together with what the levels
    /// after it declare.
    /// </summary>
    private static string[] ReachableParts(Type level)
        =>
        [
            .. level.GetInterfaces()
                    .Append(level)
                    .SelectMany(DeclaredParts)
                    .Distinct()
                    .OrderBy(name => name)
        ];

    [Test]
    public void The_Chain_Of_A_Method_Holds_A_Part_Per_Level_And_Reaches_Only_The_Parts_After_It()
    {
        // The chain is a row of levels, one for each part of a method in the order the parts depend on each other, and
        // each level asks for the part which it holds and for the parts which come after it: a part which was described
        // is never described again, and a level which was passed is out of reach.
        //
        // What a level does not do is refuse to pass the parts before it by: a method which holds no generic parameter,
        // no parameter and nothing to hand back is described by the level of the body alone, which is the shape most
        // templates are woven through. So the order is what the row of levels says it is, and the parts which are not
        // described keep what they hold, which is the default of the method.
        var chain = new (Type Level, string Part)[]
        {
            (typeof(MethodDecorator.IGenericParameterDecorator), nameof(MethodDecorator.IGenericParameterDecorator.WithGenericParameter)),
            (typeof(MethodDecorator.IParameterDecorator), nameof(MethodDecorator.IParameterDecorator.WithParameter)),
            (typeof(MethodDecorator.IReturnTypeDecorator), nameof(MethodDecorator.IReturnTypeDecorator.WithReturnType)),
            (typeof(MethodDecorator.IBodyDecorator), nameof(MethodDecorator.IBodyDecorator.WithBody)),
            (typeof(MethodDecorator.ITypeDecorator), nameof(MethodDecorator.ITypeDecorator.GetHandler)),
        };

        Assert.Multiple(() =>
        {
            for (var level = 0; level < chain.Length; level++)
            {
                Assert.That(DeclaredParts(chain[level].Level), Is.EqualTo(new[] {chain[level].Part}),
                    $"the level {level} of the chain asks for a part which it does not hold.");
                Assert.That(ReachableParts(chain[level].Level), Is.EquivalentTo(chain.Skip(level).Select(step => step.Part)),
                    $"the level {level} of the chain reaches a part which was described before it.");
            }
        });
    }

    #endregion

    #region The member which a chain builds

    [Test]
    public void A_Chain_Builds_The_Member_Which_It_Describes_Once()
    {
        // The member is appended to the module where the chain ends, so the chain which is asked for the handler of the
        // member again answers with the one which it built rather than appending a second member of the same name,
        // which the module would hold as two members which say the same thing.
        var assembly = Assembly.Create("DecoratorBuiltOnceAssembly");
        var handler = (AssemblyHandler) assembly.Handler;
        var module = assembly.Source.MainModule;
        var host = AddAHost(handler);

        var classChain = handler.AddClass("Class", NS, ClassFlags.Public);
        var structChain = handler.AddStruct("Struct", NS, StructFlags.Public);
        var enumChain = handler.AddEnum("Enum", NS, EnumFlags.Public);
        var methodChain = host.AddMethod("Method", MethodFlags.Public | MethodFlags.Static);
        var fieldChain = host.AddField("Field", FieldFlags.Public | FieldFlags.Static);
        var propertyChain = host.AddProperty("Property", PropertyFlags.Public);

        var classHandler = classChain.GetHandler();
        var structHandler = structChain.GetHandler();
        var enumHandler = enumChain.GetHandler();
        var methodHandler = methodChain.GetHandler();
        var fieldHandler = fieldChain.GetHandler();
        var propertyHandler = propertyChain.GetHandler();

        Assert.Multiple(() =>
        {
            Assert.That(classChain.GetHandler(), Is.SameAs(classHandler), "the chain of the class built a second class.");
            Assert.That(module.Types.Count(type => type.Name == "Class"), Is.EqualTo(1), "the module declares the class twice.");
            Assert.That(structChain.GetHandler(), Is.SameAs(structHandler), "the chain of the struct built a second struct.");
            Assert.That(module.Types.Count(type => type.Name == "Struct"), Is.EqualTo(1), "the module declares the struct twice.");
            Assert.That(enumChain.GetHandler(), Is.SameAs(enumHandler), "the chain of the enum built a second enum.");
            Assert.That(module.Types.Count(type => type.Name == "Enum"), Is.EqualTo(1), "the module declares the enum twice.");
            Assert.That(methodChain.GetHandler(), Is.SameAs(methodHandler), "the chain of the method built a second method.");
            Assert.That(host.Source.Methods.Count(source => source.Name == "Method"), Is.EqualTo(1), "the type declares the method twice.");
            Assert.That(fieldChain.GetHandler(), Is.SameAs(fieldHandler), "the chain of the field built a second field.");
            Assert.That(host.Source.Fields.Count(source => source.Name == "Field"), Is.EqualTo(1), "the type declares the field twice.");
            Assert.That(propertyChain.GetHandler(), Is.SameAs(propertyHandler), "the chain of the property built a second property.");
            Assert.That(host.Source.Properties.Count(source => source.Name == "Property"), Is.EqualTo(1), "the type declares the property twice.");
        });
    }

    [Test]
    public void A_Chain_Which_Built_The_Member_Which_It_Describes_Describes_Nothing_Further()
    {
        // What a chain describes is read where it builds the member, which is the end of the chain: a part which is
        // described afterwards is described to nothing, so it is refused rather than passed over, because a member which
        // does not hold what it was described by is a member which is woven and does not say what the description says.
        var assembly = Assembly.Create("DecoratorDescribedAfterBuildAssembly");
        var handler = (AssemblyHandler) assembly.Handler;
        var host = AddAHost(handler);

        var classChain = handler.AddClass("Class", NS, ClassFlags.Public);
        var structChain = handler.AddStruct("Struct", NS, StructFlags.Public);
        var enumChain = handler.AddEnum("Enum", NS, EnumFlags.Public);
        var methodChain = host.AddMethod("Method", MethodFlags.Public | MethodFlags.Static);
        var fieldChain = host.AddField("Field", FieldFlags.Public | FieldFlags.Static);
        var propertyChain = host.AddProperty("Property", PropertyFlags.Public);

        classChain.GetHandler();
        structChain.GetHandler();
        enumChain.GetHandler();
        methodChain.GetHandler();
        fieldChain.GetHandler();
        propertyChain.GetHandler();

        Assert.Multiple(() =>
        {
            Assert.Throws<WeavingException>(() => classChain.WithInterface(typeof(ITestInterface)), "the chain of the class described it after it was built.");
            Assert.Throws<WeavingException>(() => structChain.WithInterface(typeof(ITestInterface)), "the chain of the struct described it after it was built.");
            Assert.Throws<WeavingException>(() => enumChain.WithFlagsAttribute(), "the chain of the enum described it after it was built.");
            Assert.Throws<WeavingException>(() => methodChain.WithReturnType(typeof(int)), "the chain of the method described it after it was built.");
            Assert.Throws<WeavingException>(() => fieldChain.WithType(typeof(int)), "the chain of the field described it after it was built.");
            Assert.Throws<WeavingException>(() => propertyChain.WithType(typeof(int)), "the chain of the property described it after it was built.");
        });

        var thrown = Assert.Throws<WeavingException>(() => methodChain.WithReturnType(typeof(int)));
        Assert.That(thrown!.Message, Does.Contain("Method"), "the message does not name the member which was built.");
    }

    [Test]
    public void A_Null_Type_Is_Refused_As_A_Refusal()
    {
        // A null names no type, so a chain which is described by one is refused where it stands: the refusal is read by
        // its message alone, which is what tells it from a fault of the framework, and the message of a fault names
        // nothing of what was being woven.
        var assembly = Assembly.Create("DecoratorNullTypeAssembly");
        var handler = (AssemblyHandler) assembly.Handler;
        var classChain = handler.AddClass("Class", NS, ClassFlags.Public);
        var structChain = handler.AddStruct("Struct", NS, StructFlags.Public);

        Assert.Multiple(() =>
        {
            Assert.Throws<WeavingException>(() => classChain.WithBaseType((Type) null!), "the base type of the class was not refused.");
            Assert.Throws<WeavingException>(() => classChain.WithInterface((Type) null!), "the interface of the class was not refused.");
            Assert.Throws<WeavingException>(() => structChain.WithInterface((Type) null!), "the interface of the struct was not refused.");
        });
    }

    [Test]
    public void A_Type_Of_A_Kind_Which_This_Library_Does_Not_Build_Is_Refused_As_A_Refusal()
    {
        // The three kinds of a type are the ones this library builds, and one of another kind is one which nothing here
        // reads: it is refused where it stands, by the same reading of what a refusal is as the null above.
        var assembly = Assembly.Create("DecoratorUnknownTypeAssembly");
        var handler = (AssemblyHandler) assembly.Handler;
        var classChain = handler.AddClass("Class", NS, ClassFlags.Public);

        Assert.Throws<WeavingException>(() => classChain.WithBaseType(new ATypeOfAKindWhichIsNotOne()), "the type was not refused.");
    }

    [Test]
    public void A_Null_Of_The_Interface_Kind_Of_A_Type_Is_Refused_As_A_Refusal()
    {
        // A null is a kind which is none of the three as well, and it is the one a caller reaches without meaning to:
        // the reading of the kind names what was given, which is what a null has not.
        var assembly = Assembly.Create("DecoratorNullInterfaceTypeAssembly");
        var handler = (AssemblyHandler) assembly.Handler;
        var classChain = handler.AddClass("Class", NS, ClassFlags.Public);

        Assert.Throws<WeavingException>(() => classChain.WithBaseType((IType) null!), "the type was not refused.");
    }

    /// <summary>
    /// A type of a kind which this library does not build, which is what a caller of it can write where a type is asked
    /// for and nothing of this library reads.
    /// </summary>
    private sealed class ATypeOfAKindWhichIsNotOne : IType;

    #endregion

    #region The capture which a delegate holds

    /// <summary>
    /// A decorator which describes the body of a method and is not the one which this library builds, which is what the
    /// extension refuses to give a delegate to.
    /// </summary>
    private sealed class ForeignBodyDecorator : MethodDecorator.IBodyDecorator
    {
        /// <summary>
        /// The bodies which were described on it as the method alone, which stay empty where a delegate is refused
        /// instead of being read for the method which it holds.
        /// </summary>
        internal List<MethodInfo> Bodies { get; } = [];

        /// <inheritdoc/>
        public MethodDecorator.ITypeDecorator WithBody(DefaultMethodBody body) => this;

        /// <inheritdoc/>
        public MethodDecorator.ITypeDecorator WithBody(MethodInfo method)
        {
            Bodies.Add(method);
            return this;
        }

        /// <inheritdoc/>
        public IMethodHandler GetHandler() => throw new NotSupportedException("A decorator of a test builds nothing.");
    }

    /// <summary>
    /// A decorator which describes the accessors of a property and is not the one which this library builds, which is
    /// what the extensions refuse to give a delegate to.
    /// </summary>
    private sealed class ForeignAccessorDecorator : PropertyDecorator.IAccessorDecorator
    {
        /// <summary>
        /// The bodies which were described on it as the method alone, which stay empty where a delegate is refused
        /// instead of being read for the method which it holds.
        /// </summary>
        internal List<MethodInfo> GetterBodies { get; } = [];

        /// <inheritdoc cref="GetterBodies"/>
        internal List<MethodInfo> SetterBodies { get; } = [];

        /// <inheritdoc/>
        public PropertyDecorator.IAccessorDecorator WithGetter(DefaultPropertyBody body) => this;

        /// <inheritdoc/>
        public PropertyDecorator.IAccessorDecorator WithGetter(MethodInfo method)
        {
            GetterBodies.Add(method);
            return this;
        }

        /// <inheritdoc/>
        public PropertyDecorator.IAccessorDecorator WithSetter(DefaultPropertyBody body) => this;

        /// <inheritdoc/>
        public PropertyDecorator.IAccessorDecorator WithSetter(MethodInfo method)
        {
            SetterBodies.Add(method);
            return this;
        }

        /// <inheritdoc/>
        public IPropertyHandler GetHandler() => throw new NotSupportedException("A decorator of a test builds nothing.");
    }

    [Test]
    public void A_Body_Which_A_Delegate_Describes_Is_Refused_By_A_Decorator_Of_Another_Implementation()
    {
        // The delegate holds what the template captured, and the decorator which this library builds is the one which
        // writes it into the member being woven: another implementation holds nothing for it, so the delegate is refused
        // rather than read for the method alone, which would weave a body without the value the template read.
        var decorator = new ForeignBodyDecorator();
        var delegation = () => 41;

        var thrown = Assert.Throws<WeavingException>(() => decorator.WithBody(delegation));

        Assert.Multiple(() =>
        {
            Assert.That(thrown!.Message, Does.Contain(nameof(ForeignBodyDecorator)), "the message does not name the decorator which was given.");
            Assert.That(decorator.Bodies, Is.Empty, "the body was described from the method alone, which loses what the template captured.");
        });
    }

    [Test]
    public void A_Getter_And_A_Setter_Which_A_Delegate_Describes_Are_Refused_By_A_Decorator_Of_Another_Implementation()
    {
        // A getter and a setter are described by a delegate the same way a body is, and what the delegate holds is
        // refused for the same reason.
        var decorator = new ForeignAccessorDecorator();
        var captured = 41;

        var getter = Assert.Throws<WeavingException>(() => decorator.WithGetter(() => captured));
        var setter = Assert.Throws<WeavingException>(() => decorator.WithSetter((int value) => Console.WriteLine(value + captured)));

        Assert.Multiple(() =>
        {
            Assert.That(getter!.Message, Does.Contain(nameof(ForeignAccessorDecorator)), "the message does not name the decorator which was given.");
            Assert.That(setter!.Message, Does.Contain(nameof(ForeignAccessorDecorator)), "the message does not name the decorator which was given.");
            Assert.That(decorator.GetterBodies, Is.Empty, "the getter was described from the method alone, which loses what the template captured.");
            Assert.That(decorator.SetterBodies, Is.Empty, "the setter was described from the method alone, which loses what the template captured.");
        });
    }

    #endregion
}