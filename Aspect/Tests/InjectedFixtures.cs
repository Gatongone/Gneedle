namespace Gneedle.Aspect.Test.Fixtures;

/// <summary>
/// A type whose members are reached in every way a caller could reach one, so that an injector on a member which only
/// the type itself could reach is told apart from one on a member which any caller could.
/// </summary>
public class Target
{
    [ThrowBody] public void Public() { }
    [ThrowBody] internal void Internal() { }
    [ThrowBody] private void Private() { }
    [ThrowBody] protected void Protected() { }
    [ThrowBody] private static void PrivateStatic() { }
    [ThrowBody] private int PrivateWithResult() => 1;

    // The fields are given a value which nothing writes, because they are here to be injected into rather than to hold
    // anything, and a field which nothing assigns is one the compiler warns about.
    [MarkField] private int m_Marked = 0;
    [MarkField] internal int m_MarkedInternal = 0;

    private int m_Unmarked = 0;

    /// <summary>
    /// A property whose getter an injector replaces the body of.
    /// </summary>
    [ThrowGetterBody] public int MarkedGetter { get; set; }

    /// <summary>
    /// A property which no injector names.
    /// </summary>
    public int Plain { get; set; }

    /// <summary>
    /// A member which no injector names, whose body the injection of another member must leave alone.
    /// </summary>
    public int Untouched() => 7;

    /// <summary>
    /// The field which no injector names.
    /// </summary>
    public int ReadUnmarked() => m_Unmarked + m_Marked + m_MarkedInternal;
}

/// <summary>
/// A type which declares a member with an injector on it.
/// </summary>
public class Base
{
    [Recording] public void Inherited() { }
}

/// <summary>
/// A type which inherits the member above and declares none of its own, so that the member is walked once for its own
/// type and once more for this one when the inherited members are walked as well.
/// </summary>
public class Derived : Base;
