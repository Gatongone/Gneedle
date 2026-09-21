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
    /// <exception cref="InvalidILException">Thrown when the instructions around the name of the member are not the call which it stands for.</exception>
    /// <exception cref="ArgumentException">Thrown when the field is invalid.</exception>
    private void ParseField(string memberName, MemberSymbols memberSymbol, int currentIndex, InstructionFilter filter, MethodDefinition targetDef)
    {
        // Can't convert 'callvirt' to `Ldstr {field_name}`, because it doesn't even exist.
        if (!StackWalk.TryGetNextGetOrSet(filter.Target, currentIndex + 2, out var isGet, out var callvirtIndex))
        {
            throw new InvalidILException(string.Format(ErrorMessages.INVALID_IL, memberName));
        }

        // Detect Instance/Static patterns to determine skip count and declaring type.
        var skipStaticFromCount = 0;
        TypeDefinition? declaringTypeFromPattern = null;
        // The type of the instance which the template reached the field through, which names the instantiation of the
        // type which declares the field where that type declares parameters, rather than the definition of it.
        TypeReference? namedInstance = null;
        // The instance which the template reached the field through, which is the receiver of it where the field is not
        // static: the sequence which builds the instance is dropped, so the value it holds has to stand where the member
        // takes a receiver.
        Instruction? receiverIns = null;
        StackWalk.InstanceValue? instance = null;

        if (memberSymbol.HasFlag(MemberSymbols.Instance) && TryGetInstanceValue(filter, currentIndex, targetDef, out var value))
        {
            instance    = value;
            receiverIns = value.Load;
            // The type of the instance may stand for the type of another assembly, in which case the field is looked up on
            // the real one. A value whose type the walk cannot tell names no type to look one up on, which the lookup
            // below refuses in the same way as a sequence which was not read at all.
            var instanceType = value.Load is { } load ? StackWalk.GetArgType(Context, load, targetDef) : GetValueType(filter, value.Last, targetDef);
            if (instanceType != null)
            {
                declaringTypeFromPattern = instanceType.ResolveDefinition(Source.Module);
                namedInstance            = instanceType;
            }
        }
        else if (memberSymbol.HasFlag(MemberSymbols.Static) && TypeNamedByAStaticFrom(filter, currentIndex) is { } staticType)
        {
            skipStaticFromCount      = 2;
            declaringTypeFromPattern = staticType;
        }

        // A field which the template reaches through `This` or `Base` belongs to the woven type or to a base type of it,
        // and the instance which those symbols stand for is the one which the body is a member of.
        namedInstance = InstanceNamedBy(memberSymbol, namedInstance);

        // Whether the instance which the template reached the field through is a value which it computed where it stands,
        // rather than a load of one of its arguments: the load is written where the name of the field stands, and a value
        // which was computed is not held anywhere else than where it was computed.
        var instanceIsComputed = instance is {Load: null};

        var field = memberSymbol.HasFlag(MemberSymbols.Base)
            ? DeclaringTypeHandler.GetFieldInBase(memberName)
            : memberSymbol.HasFlag(MemberSymbols.Instance) || memberSymbol.HasFlag(MemberSymbols.Static)
                // The field of an Instance or a Static symbol is one of another type than the member being woven, and the
                // sequence which leads to the name of it is what names that type: a sequence which the weaving does not
                // recognize names none, so the name is refused rather than looked up on the member being woven, which
                // holds a field of that name by coincidence at most.
                ? DeclaringTypeHandler.AssemblyHandler.GetFieldFromType(declaringTypeFromPattern ?? throw new ArgumentException(string.Format(ErrorMessages.INVALID_FIELD, memberName)), memberName)
                : DeclaringTypeHandler.GetFieldInThisOrABaseType(memberName);
        if (field == null)
        {
            throw new ArgumentException(string.Format(ErrorMessages.INVALID_FIELD, memberName));
        }

        // The field is reached through the type which the sequence named, which is the member being woven for the
        // symbols which carry no such sequence: the branch above refuses an Instance or a Static which named none.
        // A handle which the template holds in a local is read and written through that local rather than where the
        // name stands, so what the name stands for is the handle itself: nothing of it is written, and every accessor
        // which a read of the local is the receiver of is written as the field instead.
        var held = StackWalk.HeldLocal(filter.Target, currentIndex + 1);
        var heldAccessors = held is { } handle ? StackWalk.AccessorsOfAHeldHandle(filter.Target, handle.Local) : null;
        if (held != null && heldAccessors == null)
        {
            throw new ArgumentException(string.Format(ErrorMessages.INVALID_HELD_HANDLE, memberName));
        }

        var declaringType = declaringTypeFromPattern ?? DeclaringTypeHandler.Source;
        // The field belongs to the definition of the type which the template named an instance of, and that type declares
        // a parameter of its own: the reference names the instantiation which was named rather than that definition,
        // which is what the reference to a member of such a type is written as wherever one is reached.
        var namedInstantiation = namedInstance is not null && field.DeclaringType.HasGenericParameters
            ? InstantiationOf(field.DeclaringType, namedInstance)
            : null;
        var fieldRef = namedInstantiation is { } instantiation
            ? new FieldReference(field.Name, field.FieldType, instantiation)
            : field.ContainsGenericParameter
                // If the field contains generic parameter, we need to make a new FieldReference with the generic instance type of declaring type as its DeclaringType.
                // Related to issue: https://github.com/jbevain/cecil/issues/954
                ? new FieldReference(field.Name, field.FieldType, declaringType.MakeGenericInstanceType([.. declaringType.GenericParameters.Select(static p => (TypeReference) p)]))
                // Otherwise we can directly import the field definition as reference.
                : ModuleLock.Import(Source.Module, field);
        var isStatic = field.Resolve().IsStatic;

        // A read of the local stands where it stands and is written as the receiver of the field, which is the load of the
        // argument the instance was named by: a value which the template computed is one value in one place, and no read
        // of the local could be written as it.
        if (heldAccessors != null && instanceIsComputed && !isStatic)
        {
            throw new ArgumentException(string.Format(ErrorMessages.INVALID_HELD_HANDLE, memberName));
        }

        // The array which carried the value of the instance is dropped, because what the field is reached through is the
        // value itself rather than a handle which holds it. The value goes with it where the field takes no receiver, or
        // where the load of it is written where the name stands instead: a value which is read for nothing is not left on
        // the stack.
        if (instance is { } heldValue)
        {
            for (var i = heldValue.First - 4; i < heldValue.First; i++)
            {
                filter.Skip(i);
            }

            for (var i = heldValue.Last + 1; i < currentIndex; i++)
            {
                filter.Skip(i);
            }

            if (heldValue.Load != null || isStatic)
            {
                for (var i = heldValue.First; i <= heldValue.Last; i++)
                {
                    filter.Skip(i);
                }
            }
        }

        // Skip the Static.From sequence if this is Static.Field.
        if (skipStaticFromCount > 0)
        {
            for (var i = currentIndex - skipStaticFromCount; i < currentIndex; i++)
            {
                filter.Skip(i);
            }
        }

        Instruction AccessorOf(bool isGet)
            => Instruction.Create(isGet
                    // callvirt instance void [Gneedle.Inject]Gneedle.Inject.ValuableMember::Get(object) -> ldfld/ldsfld class {field_type} {declaring_type}::{field_name}
                    ? isStatic ? OpCodes.Ldsfld : OpCodes.Ldfld
                    // callvirt instance void [Gneedle.Inject]Gneedle.Inject.ValuableMember::Set(object) -> stfld/stsfld class {field_type} {declaring_type}::{field_name}
                    : isStatic
                        ? OpCodes.Stsfld
                        : OpCodes.Stfld,
                fieldRef);

        if (heldAccessors is { } accessors)
        {
            // ldstr {field_name} -> nop, because the name is not what the field is reached through: every read of the
            // local is, and each of them stands where it stood.
            filter.Skip(currentIndex);

            // Skip `call class [Gneedle.Inject]Gneedle.Inject.ValuableMember [Gneedle.Inject]Gneedle.Inject.This::Field(string)`
            filter.Skip(currentIndex + 1);

            // Skip the store of the handle which the placeholder handed back. The local which it would have written is
            // the one which the weaving empties, so nothing of the handle is left in the body.
            filter.Skip(held!.Value.Store);

            foreach (var (read, accessor, accessorIsGet) in accessors)
            {
                // The read of the local is the receiver of the accessor, and it is written as the receiver of the
                // field, which a field of no instance takes none of.
                if (isStatic)
                {
                    filter.Skip(read);
                }
                else
                {
                    filter.Replace(read, CreateReceiver(receiverIns, targetDef));
                }

                filter.Replace(accessor, AccessorOf(accessorIsGet));
            }

            return;
        }

        if (!isStatic && !instanceIsComputed)
        {
            // ldstr {field_name} -> the argument which holds the instance the field is read off
            filter.Replace(currentIndex, CreateReceiver(receiverIns, targetDef));
        }
        else
        {
            // ldstr {field_name} -> nop, because the instance stands where the template computed it rather than where the
            // name stands, and a field of no instance takes no receiver at all.
            filter.Skip(currentIndex);
        }

        // Skip `call class [Gneedle.Inject]Gneedle.Inject.ValuableMember [Gneedle.Inject]Gneedle.Inject.This::Field(string)`
        filter.Skip(currentIndex + 1);

        filter.Replace(callvirtIndex, AccessorOf(isGet));
    }
}