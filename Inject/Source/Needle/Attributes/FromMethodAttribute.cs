namespace Gneedle.Inject;

[AttributeUsage(AttributeTargets.Parameter)]
public class FromMethodAttribute : Attribute
{
    /// <summary>
    /// 
    /// </summary>
    /// <param name="genericParameterPosition">Method generic parameter position which index start from 0.</param>
    public FromMethodAttribute(ushort genericParameterPosition) { }
}