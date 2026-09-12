namespace Gneedle.Inject;

/// <summary>
/// Thrown when a member which stands for one of the type which is woven is called where it is written rather than from
/// a body which the weaving wrote, which is what the member of the template does when the weaving never reached it.
/// </summary>
public class InjectionNotEffectiveException() : Exception(ErrorMessages.INJECTION_NOT_EFFECTIVE);