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
    /// <param name="targetDef">The template method which the instructions are copied from.</param>
    /// <exception cref="InvalidILException">Thrown when the nearest 'callvirt' to `Ldstr {field_name}` doesn't exist.</exception>
    /// <exception cref="ArgumentException">Thrown when the field is invalid.</exception>
    private void ParseField(string memberName, MemberSymbols memberSymbol, int currentIndex, InstructionFilter filter, MethodDefinition targetDef)
    {
        // Can't convert 'callvirt' to `Ldstr {field_name}`, because it doesn't even exist.
        if (!TryGetNextGetOrSet(filter.Target, currentIndex + 2, out var isGet, out var callvirtIndex))
        {
            throw new InvalidILException();
        }

        // Detect Object/Static patterns to determine skip count and declaring type.
        var skipArrayInitCount = 0;
        var skipStaticFromCount = 0;
        TypeDefinition? declaringTypeFromPattern = null;

        if (memberSymbol.HasFlag(MemberSymbols.Object) && currentIndex >= 1)
        {
            var prevIns = filter.Target[currentIndex - 1];
            if (prevIns.OpCode == OpCodes.Newobj && prevIns.Operand is MethodReference { Name: ".ctor", DeclaringType: var declType }
                && declType.FullName == Object.TYPE_NAME)
            {
                var baseIdx = currentIndex - 7;
                if (baseIdx >= 0
                    && filter.Target[baseIdx].OpCode.Code == Code.Ldc_I4_1
                    && filter.Target[baseIdx + 1].OpCode == OpCodes.Newarr
                    && filter.Target[baseIdx + 2].OpCode == OpCodes.Dup
                    && filter.Target[baseIdx + 3].OpCode.Code == Code.Ldc_I4_0
                    && (filter.Target[baseIdx + 4].OpCode.Code is Code.Ldarg or Code.Ldarg_0 or Code.Ldarg_1 or Code.Ldarg_2 or Code.Ldarg_3 or Code.Ldarg_S)
                    && filter.Target[baseIdx + 5].OpCode == OpCodes.Stelem_Ref)
                {
                    skipArrayInitCount = 7;
                    var instanceIns = filter.Target[baseIdx + 4];
                    var argType = GetArgType(instanceIns, targetDef) ?? throw new ArgumentException(string.Format(ErrorMessages.INVALID_FIELD, memberName));
                    // The instance type may stand for the type of another assembly, in which case the field is looked up on the real one.
                    declaringTypeFromPattern = argType.ResolveDefinition(Source.Module);
                }
            }
        }
        else if (memberSymbol.HasFlag(MemberSymbols.Static) && currentIndex >= 2)
        {
            var callFromIns = filter.Target[currentIndex - 1];
            if (callFromIns.OpCode == OpCodes.Call && callFromIns.Operand is MethodReference { Name: "From", DeclaringType: var declType }
                && declType.FullName == Static.TYPE_NAME)
            {
                var ldstrIns = filter.Target[currentIndex - 2];
                if (ldstrIns.OpCode == OpCodes.Ldstr && ldstrIns.Operand is string fullTypeName)
                {
                    skipStaticFromCount = 2;
                    declaringTypeFromPattern = DeclaringTypeHandler.AssemblyHandler.GetCecilType(fullTypeName).Definition;
                }
            }
        }

        var declaringType = declaringTypeFromPattern ?? DeclaringTypeHandler.Source;

        var field = memberSymbol.HasFlag(MemberSymbols.Base)
            ? DeclaringTypeHandler.GetFieldInBase(memberName)
            : memberSymbol.HasFlag(MemberSymbols.Object) || memberSymbol.HasFlag(MemberSymbols.Static)
                ? DeclaringTypeHandler.AssemblyHandler.GetFieldFromType(declaringType, memberName)
                : DeclaringTypeHandler.GetFieldInThis(memberName);
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

        // Skip the array init sequence if this is Object.Field with new Object(param).
        if (skipArrayInitCount > 0)
        {
            for (int i = currentIndex - skipArrayInitCount; i < currentIndex; i++)
            {
                filter.Skip(i);
            }
        }

        // Skip the Static.From sequence if this is Static.Field.
        if (skipStaticFromCount > 0)
        {
            for (int i = currentIndex - skipStaticFromCount; i < currentIndex; i++)
            {
                filter.Skip(i);
            }
        }

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
            // callvirt instance void [Gneedle.Inject]Gneedle.Inject.ValuableMember::Get(object) -> ldfld/ldsfld class {field_type} {declaring_type}::{field_name}
            ? Instruction.Create(isStatic ? OpCodes.Ldsfld : OpCodes.Ldfld, fieldRef)
            // callvirt instance void [Gneedle.Inject]Gneedle.Inject.ValuableMember::Set(object) -> stfld/stsfld class {field_type} {declaring_type}::{field_name}
            : Instruction.Create(isStatic ? OpCodes.Stsfld : OpCodes.Stfld, fieldRef));
    }
}