namespace Gneedle.Inject;

[AttributeUsage(AttributeTargets.Parameter)]
public class FromTypeAttribute : Attribute
{
    /// <summary>
    /// 
    /// </summary>
    /// <param name="genericParameterPosition">Type generic parameter position which index start from 0.</param>
    public FromTypeAttribute(ushort genericParameterPosition) { }
}