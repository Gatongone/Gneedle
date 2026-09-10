namespace Gneedle.Inject;

partial class MethodHandler
{
    /// <summary>
    /// Parse the symbol to actual property operation.
    /// </summary>
    /// <example>
    /// When the symbol is property getting, the ILCode of field setting must look like following:
    /// <code>
    /// IL_0001: ldstr "{property_name}"
    /// IL_0006: call class [Gneedle.Inject]Gneedle.Inject.ValuableMember [Gneedle.Inject]Gneedle.Inject.This::Property(string)
    /// IL_0026: callvirt instance void [Gneedle.Inject]Gneedle.Inject.ValuableMember::Get(object)
    /// </code>
    /// Then we must make it look like the following:
    /// <code>
    /// IL_0001: ldarg.0 // this, only when the property is not static
    /// IL_0006: call instance class {property_type} {declaring_type}::get_{property_name}()
    /// </code>
    /// <para/>
    /// When it is property setting, the ILCode of field setting must look like following:
    /// <code>
    /// IL_0001: ldstr "{property_name}"
    /// IL_0006: call class [Gneedle.Inject]Gneedle.Inject.ValuableMember [Gneedle.Inject]Gneedle.Inject.This::Property(string)
    /// ...
    /// IL_0026: callvirt instance void [Gneedle.Inject]Gneedle.Inject.ValuableMember::Set(object)
    /// </code>
    /// Then we must make it look like the following:
    /// <code>
    /// IL_0001: ldarg.0 // this, only when the field is not static
    /// ...
    /// IL_0026: call instance class void {declaring_type}::set_{property_name}({property_type})
    /// </code>
    /// </example>
    /// <param name="memberName">Name of the member.</param>
    /// <param name="memberSymbol">Member flags about the member kind and its property.</param>
    /// <param name="currentIndex">Index of the instruction of `ldstr {member_name}`.</param>
    /// <param name="filter">The final instruction's container.</param>
    /// <exception cref="InvalidILException">Thrown when the nearest 'callvirt' to `Ldstr {field_name}` doesn't exist.</exception>
    /// <exception cref="ArgumentException">Thrown when the property is invalid.</exception>
    private void ParseProperty(string memberName, MemberSymbols memberSymbol, int currentIndex, InstructionFilter filter, MethodDefinition targetDef)
    {
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
                    var argType = GetArgType(instanceIns, targetDef) ?? throw new ArgumentException(string.Format(ErrorMessages.INVALID_PROPERTY, memberName));
                    // The instance type may stand for the type of another assembly, in which case the property is looked up on the real one.
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

        var propertyDef = memberSymbol.HasFlag(MemberSymbols.Base)
            ? DeclaringTypeHandler.GetPropertyInBase(memberName)
            : memberSymbol.HasFlag(MemberSymbols.Object) || memberSymbol.HasFlag(MemberSymbols.Static)
                ? DeclaringTypeHandler.AssemblyHandler.GetPropertyFromType(declaringTypeFromPattern ?? throw new ArgumentException(string.Format(ErrorMessages.INVALID_PROPERTY, memberName)), memberName)
                : DeclaringTypeHandler.GetPropertyInThis(memberName);
        if (propertyDef == null)
        {
            throw new ArgumentException(string.Format(ErrorMessages.INVALID_PROPERTY, memberName));
        }

        // Skip the array init sequence if this is Object.Property with new Object(param).
        if (skipArrayInitCount > 0)
        {
            for (int i = currentIndex - skipArrayInitCount; i < currentIndex; i++)
            {
                filter.Skip(i);
            }
        }

        // Skip the Static.From sequence if this is Static.Property.
        if (skipStaticFromCount > 0)
        {
            for (int i = currentIndex - skipStaticFromCount; i < currentIndex; i++)
            {
                filter.Skip(i);
            }
        }

        if (propertyDef.GetMethod is not {IsStatic: true} || propertyDef.SetMethod is not {IsStatic: true})
        {
            // ldstr {property_name} -> ldarg.0
            filter.Replace(currentIndex, Instruction.Create(OpCodes.Ldarg_0));
        }
        else
        {
            // ldstr {property_name} -> nop
            filter.Skip(currentIndex);
        }

        // Skip `call class [Gneedle.Inject]Gneedle.Inject.ValuableMember [Gneedle.Inject]Gneedle.Inject.This::Property(string)`
        filter.Skip(currentIndex + 1);

        // Process property value getting.
        if (isGet)
        {
            if (propertyDef.GetMethod == null) throw new ArgumentException(string.Format(ErrorMessages.NON_GET_METHOD, propertyDef.Name));

            // callvirt instance void [Gneedle.Inject]Gneedle.Inject.ValuableMember::Get(object) -> call instance class {property_type} {declaring_type}::get_{property_name}()
            var getMethod = Source.Module.ImportReference(propertyDef.GetMethod);
            filter.Replace(callvirtIndex, Instruction.Create(propertyDef.GetMethod.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, getMethod));
        }
        // Process property value setting.
        else
        {
            if (propertyDef.SetMethod == null) throw new ArgumentException(string.Format(ErrorMessages.NON_SET_METHOD, propertyDef.Name));

            // callvirt instance void [Gneedle.Inject]Gneedle.Inject.ValuableMember::Set(object) -> call instance class void {declaring_type}::set_{property_name}({property_type})
            var setMethod = Source.Module.ImportReference(propertyDef.SetMethod);
            filter.Replace(callvirtIndex, Instruction.Create(propertyDef.SetMethod.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, setMethod));
        }
    }
}