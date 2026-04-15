namespace Gneedle.Inject;

partial class MethodHandler
{
    /// <summary>
    /// Parse the symbol to actual field operation.
    /// </summary>
    /// <example>
    /// When the symbol is field loading, the ILCode of field setting must look like following:
    /// <code>
    /// IL_0001: ldstr "{field_name}"
    /// IL_0006: call class [Gneedle.Inject]Gneedle.Inject.ValuableMember [Gneedle.Inject]Gneedle.Inject.This::Field(string)
    /// IL_0026: callvirt instance void [Gneedle.Inject]Gneedle.Inject.ValuableMember::Get(object)
    /// </code>
    /// Then we must make it look like the following:
    /// <code>
    /// IL_0001: ldarg.0 // this, when the field is not static
    /// IL_0006: ldfld class {field_type} {declaring_type}::{field_name}
    /// </code>
    /// <para/>
    /// When it is field setting, the ILCode of field setting must look like following:
    /// <code>
    /// IL_0001: ldstr "{field_name}"
    /// IL_0006: call class [Gneedle.Inject]Gneedle.Inject.ValuableMember [Gneedle.Inject]Gneedle.Inject.This::Field(string)
    /// ...
    /// IL_0026: callvirt instance void [Gneedle.Inject]Gneedle.Inject.ValuableMember::Set(object)
    /// </code>
    /// Then we must make it look like the following:
    /// <code>
    /// IL_0001: ldarg.0 // this, only when the field is not static
    /// ...
    /// IL_0026: stfld class {field_type} {declaring_type}::{field_name}
    /// </code>
    /// </example>
    /// <param name="memberName">Name of the member.</param>
    /// <param name="memberSymbol">Member flags about the member kind and its property.</param>
    /// <param name="currentIndex">Index of the instruction of `ldstr {member_name}`.</param>
    /// <param name="filter">The final instruction's container.</param>
    /// <exception cref="InvalidILException">Thrown when the nearest 'callvirt' to `Ldstr {field_name}` doesn't exist.</exception>
    /// <exception cref="ArgumentException">Thrown when the field is invalid.</exception>
    private void ParseField(string memberName, MemberSymbols memberSymbol, int currentIndex, InstructionFilter filter)
    {
        // Can't convert 'callvirt' to `Ldstr {field_name}`, because it doesn't even exist.
        if (!TryGetNextGetOrSet(filter.Target, currentIndex + 2, out var isGet, out var callvirtIndex))
        {
            throw new InvalidILException();
        }

        var declaringType = DeclaringTypeHandler.Source;

        var field = memberSymbol.HasFlag(MemberSymbols.Base) ? DeclaringTypeHandler.GetFieldInBase(memberName) : DeclaringTypeHandler.GetFieldInThis(memberName);
        if (field == null)
        {
            throw new ArgumentException(string.Format(ErrorMessages.INVALID_FIELD, memberName));
        }
        var fieldRef = field.ContainsGenericParameter
            // If the field contains generic parameter, we need to make a new FieldReference with the generic instance type of declaring type as its DeclaringType.
            // Related to issue: https://github.com/jbevain/cecil/issues/954
            ? new FieldReference(field.Name, field.FieldType, declaringType.MakeGenericInstanceType(declaringType.GenericParameters.Select(static p => (TypeReference) p).ToArray()))
            // Otherwise we can directly import the field definition as reference.
            : Source.Module.ImportReference(field);
        var isStatic = field.Resolve().IsStatic;

        if (!isStatic)
        {
            // ldstr {field_name} -> ldarg.0
            filter.Replace(currentIndex, Instruction.Create(OpCodes.Ldarg_0));
        }
        else
        {
            // ldstr {field_name} -> nop
            filter.Skip(currentIndex);
        }

        // Skip `call class [Gneedle.Inject]Gneedle.Inject.ValuableMember [Gneedle.Inject]Gneedle.Inject.This::Field(string)`
        filter.Skip(currentIndex + 1);

        filter.Replace(callvirtIndex, isGet
            // callvirt instance void [Gneedle.Inject]Gneedle.Inject.ValuableMember::Set(object) -> stfld/stsfld class {field_type} {declaring_type}::{field_name}
            ? Instruction.Create(isStatic ? OpCodes.Ldsfld : OpCodes.Ldfld, fieldRef)
            // callvirt instance void [Gneedle.Inject]Gneedle.Inject.ValuableMember::Get(object) -> ldfld/ldsfld class {field_type} {declaring_type}::{field_name}
            : Instruction.Create(isStatic ? OpCodes.Stsfld : OpCodes.Stfld, fieldRef));
    }
}