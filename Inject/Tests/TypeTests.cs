using Mono.Cecil;

namespace Gneedle.Inject.Test;

[TestFixture]
public class TypeTests
{
    [Test]
    public void ToIType_With_GenericType()
    {
        // Type with generic parameter.
        var genericType = typeof(GenericTestClass<>).ToIType() as GenericType;
        Assert.That(genericType, Is.Not.Null);
        Assert.That(genericType.Type == typeof(GenericTestClass<>), Is.True);
        var parameter = genericType.GenericArguments[0] as GenericParameterType;
        Assert.That(parameter, Is.Not.Null);
        Assert.That(parameter.TypeName, Is.EqualTo("T"));

        // Type with generic argument.
        var genericType2 = typeof(GenericTestClass<string>).ToIType() as GenericType;
        Assert.That(genericType2, Is.Not.Null);
        Assert.That(genericType2.Type, Is.EqualTo(typeof(GenericTestClass<>)));
        var argument = genericType2.GenericArguments[0] as NongenericType;
        Assert.That(argument, Is.Not.Null);
        Assert.That(argument.Type, Is.EqualTo(typeof(string)));
    }

    [Test]
    public void ToIType_With_NonGenericType()
    {
        // Type check.
        var type = typeof(NonGenericTestClass).ToIType() as NongenericType;
        Assert.That(type, Is.Not.Null);

        // Field check.
        Assert.That(type.Type, Is.EqualTo(typeof(NonGenericTestClass)));
    }

    [Test]
    public void CreateGenericType_With_NonGenericType()
    {
        Assert.Throws<WeavingException>(() => _ = new GenericType(typeof(NonGenericTestClass), new GenericParameterType("Test")));
    }

    [Test]
    public void TryGetSystemType_With_A_Description_Which_Names_A_Type_Of_The_Runtime()
    {
        Assert.Multiple(() =>
        {
            Assert.That(typeof(int).ToIType().TryGetSystemType(out var nonGenericType), Is.True);
            Assert.That(nonGenericType, Is.EqualTo(typeof(int)));

            // The description of an instance is the definition of the type and the arguments of it, so the type which is
            // answered with is the one the definition is made into by those arguments.
            Assert.That(typeof(List<int>).ToIType().TryGetSystemType(out var genericType), Is.True);
            Assert.That(genericType, Is.EqualTo(typeof(List<int>)));

            // A description which names a type by its name alone is read the way the runtime reads a name of its own.
            Assert.That(new ReferencedType(typeof(string).FullName!).TryGetSystemType(out var referencedType), Is.True);
            Assert.That(referencedType, Is.EqualTo(typeof(string)));
        });
    }

    [Test]
    public void TryGetSystemType_With_A_Description_Which_Names_No_Type_Of_The_Runtime()
    {
        // A type which the assembly being woven declares lies in an image which may never have been loaded, a parameter
        // stands for whatever instantiates it, and the description of an instance which holds such an argument is one
        // the definition cannot be made into: none of them is a type the runtime can hand over, and each is answered
        // with nothing rather than with a failure.
        Assert.Multiple(() =>
        {
            Assert.That(typeof(List<>).ToIType().TryGetSystemType(out var openGenericType), Is.False);
            Assert.That(openGenericType, Is.Null);

            Assert.That(new GenericParameterType("T").TryGetSystemType(out var parameterType), Is.False);
            Assert.That(parameterType, Is.Null);

            Assert.That(new ReferencedType($"{typeof(TypeTests).Namespace}.ATypeWhichIsDeclaredNowhere").TryGetSystemType(out var unknownType), Is.False);
            Assert.That(unknownType, Is.Null);
        });
    }

    [Test]
    public void CreateGenericType_With_GenericTypeArgument()
    {
        // Type with generic parameter.
        var type1 = new GenericType(typeof(GenericTestClass<>), typeof(GenericTestClass<int>));
        var arg1 = type1.GenericArguments[0] as GenericType;
        Assert.That(arg1, Is.Not.Null);
        Assert.That(arg1.Type, Is.EqualTo(typeof(GenericTestClass<>)));
        var subArg1 = arg1.GenericArguments[0] as NongenericType;
        Assert.That(subArg1, Is.Not.Null);
        Assert.That(subArg1.Type, Is.EqualTo(typeof(int)));

        // Type with generic argument.
        var type2 = new GenericType(typeof(GenericTestClass<>), typeof(GenericTestClass<>));
        var arg2 = type2.GenericArguments[0] as GenericType;
        Assert.That(arg2, Is.Not.Null);
        Assert.That(arg2.Type, Is.EqualTo(typeof(GenericTestClass<>)));
        var subArg2 = arg2.GenericArguments[0] as GenericParameterType;
        Assert.That(subArg2, Is.Not.Null);
        Assert.That(subArg2.TypeName, Is.EqualTo("T"));
    }

    [Test]
    public void CreateGenericType_With_The_Type_Alone()
    {
        // The call which names no argument is the type with the arguments which the type itself carries, which is what
        // ToIType builds out of the same type: the two spellings answer with the same argument, so a caller which
        // holds a type has a way of saying so rather than one which has to be told what the arguments of it are. It is
        // also the call which nothing answered before, because both of the overloads which take a list of arguments can
        // be called with an empty one, and a call which the compiler can read through either of two of them is one which
        // it refuses to read at all.
        var openType = new GenericType(typeof(GenericTestClass<>));

        Assert.Multiple(() =>
        {
            Assert.That(openType.Type, Is.EqualTo(typeof(GenericTestClass<>)));
            Assert.That(openType.GenericArguments.Length, Is.EqualTo(1));
            Assert.That((openType.GenericArguments[0] as GenericParameterType)?.TypeName, Is.EqualTo("T"));
        });

        // The type which is closed over its arguments carries them as well.
        var closedType = new GenericType(typeof(GenericTestClass<int>));

        Assert.Multiple(() =>
        {
            Assert.That(closedType.Type, Is.EqualTo(typeof(GenericTestClass<>)));
            Assert.That((closedType.GenericArguments[0] as NongenericType)?.Type, Is.EqualTo(typeof(int)));
            Assert.That(closedType.GetTypeName(), Is.EqualTo(typeof(GenericTestClass<int>).ToIType().GetTypeName()));
        });
    }

    [Test]
    public void CreateNonGenericType_With_GenericType()
    {
        Assert.Throws<WeavingException>(() => { new NongenericType(typeof(GenericTestClass<>)); });
        Assert.Throws<WeavingException>(() => { new NongenericType(typeof(GenericTestClass<string>)); });
    }

    [Test]
    public void GetTypeName_With_OneArgument_GenericType()
    {
        // Type with generic argument.
        var type1 = new GenericType(typeof(GenericTestClass<>), typeof(int));
        Assert.That(type1.GetTypeName(), Is.EqualTo(new TypeName(typeof(GenericTestClass<int>))));

        // Type with generic parameter.
        var type2 = new GenericType(typeof(GenericTestClass<>), "T");
        Assert.That(type2.GetTypeName(), Is.EqualTo(new TypeName(typeof(GenericTestClass<>))));

        // Type with wrapper generic argument.
        var type3 = new GenericType(typeof(GenericTestClass<>), typeof(GenericTestClass<int>));
        Assert.That(type3.GetTypeName(), Is.EqualTo(new TypeName(typeof(GenericTestClass<GenericTestClass<int>>))));

        // Type with wrapper generic parameter.
        var type4 = new GenericType(typeof(GenericTestClass<>), typeof(GenericTestClass<>));
        Assert.That(type4.GetTypeName(), Is.EqualTo(new TypeName(typeof(GenericTestClass<>).MakeGenericType(typeof(GenericTestClass<>)))));
    }

    [Test]
    public void GetTypeName_With_TwoArguments_GenericType()
    {
        // Type with generic argument.
        var type1 = new GenericType(typeof(GenericTestClass<,>), typeof(int), typeof(string));
        Assert.That(type1.GetTypeName(), Is.EqualTo(new TypeName(typeof(GenericTestClass<int, string>))));

        // Type with generic parameter.
        var type2 = new GenericType(typeof(GenericTestClass<,>), "T1", "T2");
        Assert.That(type2.GetTypeName(), Is.EqualTo(new TypeName(typeof(GenericTestClass<,>))));

        // Type with wrapper generic argument.
        var type3 = new GenericType(typeof(GenericTestClass<,>), typeof(GenericTestClass<int, string>), typeof(GenericTestClass<int, string>));
        Assert.That(type3.GetTypeName(), Is.EqualTo(new TypeName(typeof(GenericTestClass<GenericTestClass<int, string>, GenericTestClass<int, string>>))));

        // Type with wrapper generic parameter.
        var type4 = new GenericType(typeof(GenericTestClass<,>), typeof(GenericTestClass<,>), typeof(GenericTestClass<,>));
        Assert.That(type4.GetTypeName(), Is.EqualTo(new TypeName(typeof(GenericTestClass<,>)
            .MakeGenericType(typeof(GenericTestClass<,>), typeof(GenericTestClass<,>)))));
    }

    [Test]
    public void GetTypeName_With_OneArgument_GenericInstanceType()
    {
        var assembly = AssemblyDefinition.ReadAssembly(System.Reflection.Assembly.GetExecutingAssembly().Location);

        // Type with generic argument.
        var type1 = assembly.MainModule.ImportReference(typeof(GenericTestClass<int>));
        Assert.That(new TypeName(type1), Is.EqualTo(new TypeName(typeof(GenericTestClass<int>))));

        // Type with generic parameter.
        var type2 = assembly.MainModule.ImportReference(typeof(GenericTestClass<>));
        Assert.That(new TypeName(type2), Is.EqualTo(new TypeName(typeof(GenericTestClass<>))));

        // Type with wrapper generic argument.
        var type3 = assembly.MainModule.ImportReference(typeof(GenericTestClass<GenericTestClass<int>>));
        Assert.That(new TypeName(type3), Is.EqualTo(new TypeName(typeof(GenericTestClass<GenericTestClass<int>>))));

        // Type with wrapper generic argument.
        var type4 = assembly.MainModule.ImportReference(typeof(GenericTestClass<>).MakeGenericType(typeof(GenericTestClass<>)));
        Assert.That(new TypeName(type4), Is.EqualTo(new TypeName(typeof(GenericTestClass<>).MakeGenericType(typeof(GenericTestClass<>)))));
    }

    [Test]
    public void GetTypeName_With_TwoArgument_GenericInstanceType()
    {
        var assembly = AssemblyDefinition.ReadAssembly(System.Reflection.Assembly.GetExecutingAssembly().Location);

        // Type with generic argument.
        var type1 = assembly.MainModule.ImportReference(typeof(GenericTestClass<int, string>));
        Assert.That(new TypeName(type1), Is.EqualTo(new TypeName(typeof(GenericTestClass<int, string>))));

        // Type with generic parameter.
        var type2 = assembly.MainModule.ImportReference(typeof(GenericTestClass<,>));
        Assert.That(new TypeName(type2), Is.EqualTo(new TypeName(typeof(GenericTestClass<,>))));

        // Type with wrapper generic argument.
        var type3 = assembly.MainModule.ImportReference(typeof(GenericTestClass<GenericTestClass<int, string>, GenericTestClass<int, string>>));
        Assert.That(new TypeName(type3), Is.EqualTo(new TypeName(typeof(GenericTestClass<GenericTestClass<int, string>, GenericTestClass<int, string>>))));

        // Type with wrapper generic argument.
        var type4 = assembly.MainModule.ImportReference(typeof(GenericTestClass<,>)
            .MakeGenericType(typeof(GenericTestClass<,>), typeof(GenericTestClass<,>)));
        Assert.That(new TypeName(type4), Is.EqualTo(new TypeName(typeof(GenericTestClass<,>)
            .MakeGenericType(typeof(GenericTestClass<,>), typeof(GenericTestClass<,>)))));
    }
}

internal class GenericTestClass<T>;

internal class GenericTestClass<T1, T2>;

internal class NonGenericTestClass;