namespace Gneedle.Inject;

/// <summary>
/// The bound of the generic parameter tokens, which are the <c>T_</c> and the <c>M_</c> classes which this file declares.
/// </summary>
internal static class GenericTokens
{
    /// <summary>
    /// The index of the last token of each kind, which is the highest position at which a template can name a generic
    /// parameter.<para/>
    /// The tokens are declared one class at a time, and the pattern which reads a token is made from this number rather
    /// than holding one of its own: a token which is declared beyond the bound is a type which a template compiles
    /// against and the weaving reads as an ordinary type of this library instead of as a generic parameter, which is a
    /// body that is written and does not stand for what it says.
    /// </summary>
    internal const int HighestIndex = 20;
}

/// <summary>
/// The generic parameter type of the type that the index of 0.
/// </summary>
public sealed class T_0 : Instance;

/// <summary>
/// The generic parameter type of the type that the index of 1.
/// </summary>
public sealed class T_1 : Instance;

/// <summary>
/// The generic parameter type of the type that the index of 2.
/// </summary>
public sealed class T_2 : Instance;

/// <summary>
/// The generic parameter type of the type that the index of 3.
/// </summary>
public sealed class T_3 : Instance;

/// <summary>
/// The generic parameter type of the type that the index of 4.
/// </summary>
public sealed class T_4 : Instance;

/// <summary>
/// The generic parameter type of the type that the index of 5.
/// </summary>
public sealed class T_5 : Instance;

/// <summary>
/// The generic parameter type of the type that the index of 6.
/// </summary>
public sealed class T_6 : Instance;

/// <summary>
/// The generic parameter type of the type that the index of 7.
/// </summary>
public sealed class T_7 : Instance;

/// <summary>
/// The generic parameter type of the type that the index of 8.
/// </summary>
public sealed class T_8 : Instance;

/// <summary>
/// The generic parameter type of the type that the index of 9.
/// </summary>
public sealed class T_9 : Instance;

/// <summary>
/// The generic parameter type of the type that the index of 10.
/// </summary>
public sealed class T_10 : Instance;

/// <summary>
/// The generic parameter type of the type that the index of 11.
/// </summary>
public sealed class T_11 : Instance;

/// <summary>
/// The generic parameter type of the type that the index of 12.
/// </summary>
public sealed class T_12 : Instance;

/// <summary>
/// The generic parameter type of the type that the index of 13.
/// </summary>
public sealed class T_13 : Instance;

/// <summary>
/// The generic parameter type of the type that the index of 14.
/// </summary>
public sealed class T_14 : Instance;

/// <summary>
/// The generic parameter type of the type that the index of 15.
/// </summary>
public sealed class T_15 : Instance;

/// <summary>
/// The generic parameter type of the type that the index of 16.
/// </summary>
public sealed class T_16 : Instance;

/// <summary>
/// The generic parameter type of the type that the index of 17.
/// </summary>
public sealed class T_17 : Instance;

/// <summary>
/// The generic parameter type of the type that the index of 18.
/// </summary>
public sealed class T_18 : Instance;

/// <summary>
/// The generic parameter type of the type that the index of 19.
/// </summary>
public sealed class T_19 : Instance;

/// <summary>
/// The generic parameter type of the type that the index of 20.
/// </summary>
public sealed class T_20 : Instance;

/// <summary>
/// The generic parameter type of the method that the index of 0.
/// </summary>
public sealed class M_0 : Instance;

/// <summary>
/// The generic parameter type of the method that the index of 1.
/// </summary>
public sealed class M_1 : Instance;

/// <summary>
/// The generic parameter type of the method that the index of 2.
/// </summary>
public sealed class M_2 : Instance;

/// <summary>
/// The generic parameter type of the method that the index of 3.
/// </summary>
public sealed class M_3 : Instance;

/// <summary>
/// The generic parameter type of the method that the index of 4.
/// </summary>
public sealed class M_4 : Instance;

/// <summary>
/// The generic parameter type of the method that the index of 5.
/// </summary>
public sealed class M_5 : Instance;

/// <summary>
/// The generic parameter type of the method that the index of 6.
/// </summary>
public sealed class M_6 : Instance;

/// <summary>
/// The generic parameter type of the method that the index of 7.
/// </summary>
public sealed class M_7 : Instance;

/// <summary>
/// The generic parameter type of the method that the index of 8.
/// </summary>
public sealed class M_8 : Instance;

/// <summary>
/// The generic parameter type of the method that the index of 9.
/// </summary>
public sealed class M_9 : Instance;

/// <summary>
/// The generic parameter type of the method that the index of 10.
/// </summary>
public sealed class M_10 : Instance;

/// <summary>
/// The generic parameter type of the method that the index of 11.
/// </summary>
public sealed class M_11 : Instance;

/// <summary>
/// The generic parameter type of the method that the index of 12.
/// </summary>
public sealed class M_12 : Instance;

/// <summary>
/// The generic parameter type of the method that the index of 13.
/// </summary>
public sealed class M_13 : Instance;

/// <summary>
/// The generic parameter type of the method that the index of 14.
/// </summary>
public sealed class M_14 : Instance;

/// <summary>
/// The generic parameter type of the method that the index of 15.
/// </summary>
public sealed class M_15 : Instance;

/// <summary>
/// The generic parameter type of the method that the index of 16.
/// </summary>
public sealed class M_16 : Instance;

/// <summary>
/// The generic parameter type of the method that the index of 17.
/// </summary>
public sealed class M_17 : Instance;

/// <summary>
/// The generic parameter type of the method that the index of 18.
/// </summary>
public sealed class M_18 : Instance;

/// <summary>
/// The generic parameter type of the method that the index of 19.
/// </summary>
public sealed class M_19 : Instance;

/// <summary>
/// The generic parameter type of the method that the index of 20.
/// </summary>
public sealed class M_20 : Instance;