/*
 * Copyright ©2023 Gatongone
 * Author: Gatongone
 * Email: gatongone@gmail.com
 * Created On: 2023/11/18-12:26:46
 * Github: https://github.com/Gatongone
 */

using Mono.Cecil;
using GenericParameterType = Gneedle.Inject.GenericParameterType;

namespace Gneedle.Test;

[TestFixture]
public class TypeTests
{
    [Test]
    public void ToGneedleType_With_GenericType()
    {
        // Type with generic parameter.
        var t = typeof(GenericTestClass<>);
        var genericType = typeof(GenericTestClass<>).ToGneedleType() as GenericType;
        Assert.That(genericType, Is.Not.Null);
        Assert.That(genericType.Type == typeof(GenericTestClass<>), Is.True);
        var parameter = genericType.GenericArguments[0] as GenericParameterType;
        Assert.That(parameter, Is.Not.Null);
        Assert.That(parameter.TypeName, Is.EqualTo("T"));

        // Type with generic argument.
        var genericType2 = typeof(GenericTestClass<string>).ToGneedleType() as GenericType;
        Assert.That(genericType2, Is.Not.Null);
        Assert.That(genericType2.Type == typeof(GenericTestClass<>), Is.True);
        Console.WriteLine(genericType2.Type.FullName);
        var argument = genericType2.GenericArguments[0] as NongenericType;
        Assert.That(argument, Is.Not.Null);
        Assert.That(argument.Type, Is.EqualTo(typeof(string)));
    }

    [Test]
    public void ToGneedleType_With_NonGenericType()
    {
        // Type check.
        var type = typeof(NonGenericTestClass).ToGneedleType() as NongenericType;
        Assert.That(type, Is.Not.Null);

        // Field check.
        Assert.That(type.Type, Is.EqualTo(typeof(NonGenericTestClass)));
    }

    [Test]
    public void CreateGenericType_With_NonGenericType()
    {
        Assert.Catch(() => { new GenericType(typeof(NonGenericTestClass), new GenericParameterType("Test")); });
    }

    [Test]
    public void CreateGenericType_With_GenericTypeArgument()
    {
        // Type with generic parameter.
        var type1 = new GenericType(typeof(GenericTestClass<>), typeof(GenericTestClass<int>));
        var arg1 = type1.GenericArguments[0] as GenericType;
        Assert.That(arg1, Is.Not.Null);
        Assert.That(arg1.Type, Is.EqualTo(typeof(GenericTestClass<>)));
        var subArg_1 = arg1.GenericArguments[0] as NongenericType;
        Assert.That(subArg_1, Is.Not.Null);
        Assert.That(subArg_1.Type, Is.EqualTo(typeof(int)));

        // Type with generic argument.
        var type2 = new GenericType(typeof(GenericTestClass<>), typeof(GenericTestClass<>));
        var arg2 = type2.GenericArguments[0] as GenericType;
        Assert.That(arg2, Is.Not.Null);
        Assert.That(arg2.Type, Is.EqualTo(typeof(GenericTestClass<>)));
        var subArg_2 = arg2.GenericArguments[0] as GenericParameterType;
        Assert.That(subArg_2, Is.Not.Null);
        Assert.That(subArg_2.TypeName, Is.EqualTo("T"));
    }

    [Test]
    public void CreateNonGenericType_With_GenericType()
    {
        Assert.Catch(() => { new NongenericType(typeof(GenericTestClass<>)); });
        Assert.Catch(() => { new NongenericType(typeof(GenericTestClass<string>)); });
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