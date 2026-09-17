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
    /// <param name="targetDef">The template method which the instructions are copied from.</param>
    /// <exception cref="InvalidILException">Thrown when the instructions around the name of the member are not the call which it stands for.</exception>
    /// <exception cref="ArgumentException">Thrown when the property is invalid.</exception>
    private void ParseProperty(string memberName, MemberSymbols memberSymbol, int currentIndex, InstructionFilter filter, MethodDefinition targetDef)
    {
        if (!TryGetNextGetOrSet(filter.Target, currentIndex + 2, out var isGet, out var callvirtIndex))
        {
            throw new InvalidILException(string.Format(ErrorMessages.INVALID_IL, memberName));
        }

        // Detect Instance/Static patterns to determine skip count and declaring type.
        var skipStaticFromCount = 0;
        TypeDefinition? declaringTypeFromPattern = null;
        // The type which the template named the instance through, which the accessor is written on where it belongs to
        // that type itself rather than to a base type of it.
        TypeReference? namedInstance = null;
        // The instance which the template reached the property through, which is the receiver of its call where the
        // accessor which is called takes one: the sequence which builds the instance is dropped, so the value it holds has
        // to stand where the accessor takes a receiver.
        Instruction? receiverIns = null;
        InstanceValue? instance = null;

        if (memberSymbol.HasFlag(MemberSymbols.Instance) && TryGetInstanceValue(filter, currentIndex, targetDef, out var value))
        {
            instance    = value;
            receiverIns = value.Load;
            // The type of the instance may stand for the type of another assembly, in which case the property is looked up
            // on the real one. A value whose type the walk cannot tell names no type to look one up on, which the lookup
            // below refuses in the same way as a sequence which was not read at all.
            var instanceType = value.Load is { } load ? GetArgType(load, targetDef) : GetValueType(filter, value.Last, targetDef);
            if (instanceType != null)
            {
                declaringTypeFromPattern = instanceType.ResolveDefinition(Source.Module);
                // The type which the template named the instance through is written out where the accessor belongs to
                // that type itself: the value which the instance holds is one of the type as the template declared it,
                // which is an instantiation of the type which the lookup below answers with.
                namedInstance = instanceType;
            }
        }
        else if (memberSymbol.HasFlag(MemberSymbols.Static) && currentIndex >= 2)
        {
            var callFromIns = filter.Target[currentIndex - 1];
            if (callFromIns.OpCode == OpCodes.Call && callFromIns.Operand is MethodReference {Name: "From", DeclaringType: var declType}
                && declType.FullName == Static.TYPE_NAME)
            {
                var ldstrIns = filter.Target[currentIndex - 2];
                if (ldstrIns.OpCode == OpCodes.Ldstr && ldstrIns.Operand is string fullTypeName)
                {
                    skipStaticFromCount      = 2;
                    declaringTypeFromPattern = DeclaringTypeHandler.AssemblyHandler.GetCecilType(fullTypeName).Definition;
                }
            }
        }

        // Whether the instance which the template reached the property through is a value which it computed where it
        // stands, rather than a load of one of its arguments: the load is written where the name of the property stands,
        // and a value which was computed is not held anywhere else than where it was computed.
        var instanceIsComputed = instance is { Load: null };

        var propertyDef = memberSymbol.HasFlag(MemberSymbols.Base)
            ? DeclaringTypeHandler.GetPropertyInBase(memberName)
            : memberSymbol.HasFlag(MemberSymbols.Instance) || memberSymbol.HasFlag(MemberSymbols.Static)
                ? DeclaringTypeHandler.AssemblyHandler.GetPropertyFromType(declaringTypeFromPattern ?? throw new ArgumentException(string.Format(ErrorMessages.INVALID_PROPERTY, memberName)), memberName)
                : DeclaringTypeHandler.GetPropertyInThisOrABaseType(memberName);
        if (propertyDef == null)
        {
            throw new ArgumentException(string.Format(ErrorMessages.INVALID_PROPERTY, memberName));
        }

        // The accessor which is called is the one which tells whether a receiver is written, because it is the one which
        // the call below reaches: a static accessor takes no receiver, and an instance one takes the member as its own.
        // The accessor which is not called says nothing about it, and a property which holds only the one being called
        // is what makes that worth saying: reading staticness off both of them together takes the accessor which is not
        // there for one which is not static, and writes a load of `this` into a member which is static.
        var calledAccessor  = isGet ? propertyDef.GetMethod : propertyDef.SetMethod;
        var takesAReceiver  = calledAccessor is not {IsStatic: true};

        // A handle which the template holds in a local is read and written through that local rather than where the
        // name stands, so what the name stands for is the handle itself: nothing of it is written, and every accessor
        // which a read of the local is the receiver of is written as the accessor of the property instead.
        var held          = HeldLocal(filter.Target, currentIndex + 1);
        var heldAccessors = held is { } handle ? AccessorsOfAHeldHandle(filter.Target, handle.Local) : null;
        if (held != null && heldAccessors == null)
        {
            throw new ArgumentException(string.Format(ErrorMessages.INVALID_HELD_HANDLE, memberName));
        }

        // A read of the local stands where it stands and is written as the receiver of the accessor, which is the load of
        // the argument the instance was named by: a value which the template computed is one value in one place, and no
        // read of the local could be written as it.
        if (heldAccessors != null && instanceIsComputed && takesAReceiver)
        {
            throw new ArgumentException(string.Format(ErrorMessages.INVALID_HELD_HANDLE, memberName));
        }

        // The array which carried the value of the instance is dropped, because what the property is reached through is
        // the value itself rather than a handle which holds it. The value goes with it where the accessor takes no
        // receiver, or where the load of it is written where the name stands instead: a value which is read for nothing is
        // not left on the stack.
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

            if (heldValue.Load != null || !takesAReceiver)
            {
                for (var i = heldValue.First; i <= heldValue.Last; i++)
                {
                    filter.Skip(i);
                }
            }
        }

        // Skip the Static.From sequence if this is Static.Property.
        if (skipStaticFromCount > 0)
        {
            for (var i = currentIndex - skipStaticFromCount; i < currentIndex; i++)
            {
                filter.Skip(i);
            }
        }

        if (heldAccessors is { } accessors)
        {
            // ldstr {property_name} -> nop, because the name is not what the property is reached through: every read of
            // the local is, and each of them stands where it stood.
            filter.Skip(currentIndex);

            // Skip `call class [Gneedle.Inject]Gneedle.Inject.ValuableMember [Gneedle.Inject]Gneedle.Inject.This::Property(string)`
            filter.Skip(currentIndex + 1);

            // Skip the store of the handle which the placeholder handed back. The local which it would have written is
            // the one which the weaving empties, so nothing of the handle is left in the body.
            filter.Skip(held!.Value.Store);

            foreach (var (read, accessor, accessorIsGet) in accessors)
            {
                var accessorDef = AccessorOf(propertyDef, accessorIsGet);

                // The read of the local is the receiver of the accessor, and it is written as the receiver of the
                // property, which an accessor of no instance takes none of.
                if (accessorDef.IsStatic)
                {
                    filter.Skip(read);
                }
                else
                {
                    filter.Replace(read, CreateReceiver(receiverIns, targetDef));
                }

                filter.Replace(accessor, Instruction.Create(accessorDef.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, GetMethodReference(accessorDef, namedInstance)));
            }

            return;
        }

        if (takesAReceiver && !instanceIsComputed)
        {
            // ldstr {property_name} -> the argument which holds the instance the property is read off
            filter.Replace(currentIndex, CreateReceiver(receiverIns, targetDef));
        }
        else
        {
            // ldstr {property_name} -> nop, because the instance stands where the template computed it rather than where
            // the name stands, and an accessor of no instance takes no receiver at all.
            filter.Skip(currentIndex);
        }

        // Skip `call class [Gneedle.Inject]Gneedle.Inject.ValuableMember [Gneedle.Inject]Gneedle.Inject.This::Property(string)`
        filter.Skip(currentIndex + 1);

        // Process property value getting.
        if (isGet)
        {
            if (propertyDef.GetMethod == null) throw new ArgumentException(string.Format(ErrorMessages.NON_GET_METHOD, propertyDef.Name));

            // callvirt instance void [Gneedle.Inject]Gneedle.Inject.ValuableMember::Get(object) -> call instance class {property_type} {declaring_type}::get_{property_name}()
            var getMethod = GetMethodReference(propertyDef.GetMethod, namedInstance);
            filter.Replace(callvirtIndex, Instruction.Create(propertyDef.GetMethod.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, getMethod));
        }
        // Process property value setting.
        else
        {
            if (propertyDef.SetMethod == null) throw new ArgumentException(string.Format(ErrorMessages.NON_SET_METHOD, propertyDef.Name));

            // callvirt instance void [Gneedle.Inject]Gneedle.Inject.ValuableMember::Set(object) -> call instance class void {declaring_type}::set_{property_name}({property_type})
            var setMethod = GetMethodReference(propertyDef.SetMethod, namedInstance);
            filter.Replace(callvirtIndex, Instruction.Create(propertyDef.SetMethod.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, setMethod));
        }
    }

    /// <summary>
    /// The accessor which the value member of a property stands for.
    /// </summary>
    /// <param name="propertyDef">The property which is read or written.</param>
    /// <param name="isGet">Whether the value member reads the property rather than writing it.</param>
    /// <returns>The accessor of the property.</returns>
    /// <exception cref="ArgumentException">Thrown when the property holds no accessor of that kind.</exception>
    private static MethodDefinition AccessorOf(PropertyDefinition propertyDef, bool isGet)
        => isGet
            ? propertyDef.GetMethod ?? throw new ArgumentException(string.Format(ErrorMessages.NON_GET_METHOD, propertyDef.Name))
            : propertyDef.SetMethod ?? throw new ArgumentException(string.Format(ErrorMessages.NON_SET_METHOD, propertyDef.Name));
}