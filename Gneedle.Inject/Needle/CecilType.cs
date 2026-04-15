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
    public readonly TypeDefinition Definition = definition;

    /// <summary>
    /// The type source reference.
    /// </summary>
    public readonly TypeReference Reference = reference;
}