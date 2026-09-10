// Copyright ©2023 Gatongone
// Author: Gatongone
// Email: gatongone@gmail.com
// Created On: 2023/11/20-20:24:12
// Github: https://github.com/Gatongone

namespace Gneedle.Inject;

/// <summary>
/// Cecil type info.
/// </summary>
/// <param name="definition">Type definition.</param>
/// <param name="reference">Type reference.</param>
internal readonly struct CecilType(TypeDefinition definition, TypeReference reference)
{
    /// <summary>
    /// The resolved or defined type.
    /// </summary>
    /// <remarks>
    /// It is meant for looking the members up, and it is owned by the module which declares the type, so it must not be
    /// assigned to any member of the target assembly. Cecil only writes such a definition when it does not need a
    /// metadata token for it, so a leaked one either throws or corrupts the produced assembly.
    /// </remarks>
    public readonly TypeDefinition Definition = definition;

    /// <summary>
    /// The type source reference.
    /// </summary>
    /// <remarks>
    /// It is the reference which may be assigned to the target assembly, and it is guaranteed to be owned by it.
    /// </remarks>
    public readonly TypeReference Reference = reference;
}