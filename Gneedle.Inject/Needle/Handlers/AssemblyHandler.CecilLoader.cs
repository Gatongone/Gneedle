namespace Gneedle.Inject;

partial class AssemblyHandler
{
#if NETFRAMEWORK || NETCOREAPP
    // Cache for assembly definition which is imported from current assembly definition.
    private static System.Reflection.MethodInfo? s_GetAssemblyRawBytes;
#endif
    /// <summary>
    /// Convert the parameter type to TypeReference from target type.
    /// </summary>
    /// <param name="target">The parameter type's owner.</param>
    /// <param name="parameterType">The type which need to convert to TypeReference.</param>
    /// <param name="methodGenericParameters">Generic parameters from method.</param>
    /// <returns>Resolved parameter type.</returns>
    internal TypeReference ResolveParameterType(TypeReference target, IType parameterType, IEnumerable<GenericParameter>? methodGenericParameters = null)
    {
        switch (parameterType)
        {
            case NongenericType nongenericType:
                // NongenericType just return definition.
                return GetCecilType(nongenericType.Type).Definition;
            case GenericParameterType genericParameterType:
                // Get generic parameter from method or target type.
                return methodGenericParameters?.FirstOrDefault(param => genericParameterType.TypeName.Equals(param.FullName))
                    ?? target.GenericParameters.FirstOrDefault(param => genericParameterType.TypeName.Equals(param.FullName))
                    ?? throw new ArgumentException(string.Format(ErrorMessages.INVALID_GENERIC_PARAMETER, genericParameterType.TypeName));
            case GenericType genericType:
                // Generic type should be recursively resolve its arguments.
                var parameterTypeDef = GetCecilType(genericType.Type);
                // Resolve all arguments.
                var arguments = genericType.GenericArguments
                                           .Select(argument => ResolveParameterType(target, argument, methodGenericParameters))
                                           .ToArray();
                // Create generic instance.
                return Assembly.Source.MainModule
                               .ImportReference(parameterTypeDef.Definition)
                               .MakeGenericInstanceType(arguments);
            default: throw new ArgumentOutOfRangeException(nameof(parameterType));
        }
    }

    /// <summary>
    /// Get cecil type from type's type name which was imported from current assembly definition.
    /// If the type's assembly was never been referenced, then it would be appended.
    /// </summary>
    /// <param name="type">The type which need to be converted.</param>
    /// <returns>The cecil type from current definition.</returns>
    internal CecilType GetCecilType(IType type) => GetCecilType(type.GetTypeName());

    /// <summary>
    /// Get cecil type from type name which was imported from current assembly definition.
    /// If the type's assembly was never been referenced, then it would be appended.
    /// </summary>
    /// <param name="typeName">Name of the type which need to be converted.</param>
    /// <exception cref="ArgumentException">Thrown when can't get from runtime type with the type name.</exception>
    /// <returns>The cecil type from current definition.</returns>
    internal CecilType GetCecilType(TypeName typeName) => GetCecilType(typeName.ToString());

    /// <summary>
    /// Get cecil type from type name which was imported from current assembly definition.
    /// If the type's assembly was never been referenced, then it would be appended.
    /// </summary>
    /// <param name="typeName">Name of the type which need to be converted.</param>
    /// <exception cref="ArgumentException">Thrown when can't get from runtime type with the type name.</exception>
    /// <returns>The cecil type from current definition.</returns>
    internal CecilType GetCecilType(string typeName)
    {
        // Check assembly has be appended to cache.
        if (m_TypeCache.TryGetValue(typeName, out var cecilType)) return cecilType;

        var type = Type.GetType(typeName);

        if (type != null) return GetCecilType(type);

        var typeDef = Assembly.Source.Modules.SelectMany(module => module.Types).FirstOrDefault(t => typeName == t.Name);
        return typeDef != null ? GetCecilType(typeDef) : throw new ArgumentException(ErrorMessages.INVALID_TYPE_NAME);
    }

    /// <summary>
    /// Get cecil type from <see cref="Mono.Cecil.TypeReference"/> which was imported from current assembly definition.
    /// If the type's assembly was never been referenced, then it would be appended.
    /// </summary>
    /// <param name="typeRef">The type which need to be converted.</param>
    /// <returns>The cecil type from current definition</returns>
    internal CecilType GetCecilType(TypeReference typeRef)
    {
        // If has imported, then return from cache.
        if (m_TypeCache.TryGetValue(new TypeName(typeRef).ToString(), out var cecilType)) return cecilType;

        // Get assembly name.
        var assemblyName = typeRef.Module.Assembly.Name.FullName;

        // Check assembly has be appended to cache.
        if (!m_AssemblyCache.TryGetValue(assemblyName, out var assemblyDef))
        {
            // Get target assembly definition.
            assemblyDef = typeRef.Module.Assembly;
            // Append to cache.
            m_AssemblyCache[assemblyName] = assemblyDef;
            //Add to Reference.
            AddReference(assemblyDef);
        }

        // Import type ref and add to type cache.
        cecilType = new CecilType(typeRef.Resolve(), typeRef);
        m_TypeCache.Add(new TypeName(typeRef).ToString(), cecilType);
        return cecilType;
    }

    /// <summary>
    /// Get cecil type from <see cref="System.Type">System.Type</see> which was imported from current assembly definition.
    /// If the type's assembly was never been referenced, then it would be appended.
    /// </summary>
    /// <param name="type">The type which need to be converted.</param>
    /// <returns>The cecil type from current definition</returns>
    internal CecilType GetCecilType(Type type)
    {
        // If has imported, then return from cache.
        if (m_TypeCache.TryGetValue(new TypeName(type), out var cecilType)) return cecilType;

        // Load assembly from location or cache.
        var assemblyName = type.Assembly.GetName().FullName;

        // Check assembly has be appended to cache.
        if (!m_AssemblyCache.TryGetValue(assemblyName, out var assemblyDef))
        {
            // Get bytes that is a COFF-based image containing an emitted assembly.
            if (!TryGetAssemblyRawBytes(type.Assembly, out var rawBytes))
            {
                throw new NotSupportException(ErrorMessages.TARGET_FRAMEWORK_NOT_SUPPORTED);
            }

            // Create assembly definition from raw bytes.
            using var memoryStream = new MemoryStream(rawBytes);
            assemblyDef = AssemblyDefinition.ReadAssembly(memoryStream, new ReaderParameters
            {
                InMemory    = true,
                ReadWrite   = false,
                ReadingMode = ReadingMode.Deferred
            });
            memoryStream.Close();

            // Append to cache.
            m_AssemblyCache[assemblyName] = assemblyDef;
            // Reference target assembly.
            AddReference(assemblyDef);
        }

        // Import type ref and add to type cache.
        var targetTypeRef = assemblyDef.MainModule.ImportReference(type);
        cecilType = new CecilType(targetTypeRef.Resolve(), targetTypeRef);
        m_TypeCache.Add(new TypeName(type).ToString(), cecilType);
        return cecilType;
    }

    /// <summary>
    /// Get bytes from which is a COFF-based image containing an emitted assembly.
    /// </summary>
    /// <param name="assembly">The assembly which need to get raw bytes.</param>
    /// <param name="rawBytes">Bytes that is a COFF-based image containing an emitted assembly.</param>
    /// <returns>True when there is any way to get raw bytes.</returns>
    private static bool TryGetAssemblyRawBytes(System.Reflection.Assembly assembly, out byte[] rawBytes)
    {
        rawBytes = Array.Empty<byte>();
#if NETFRAMEWORK // On .NET Framework, GetRawBytes is a non-public method of System.Reflection.Assembly, which can be used to get raw bytes of assembly.
        s_GetAssemblyRawBytes ??= assembly.GetType().GetMethod("GetRawBytes", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        rawBytes = s_GetAssemblyRawBytes?.Invoke(assembly, null) as byte[] ?? rawBytes;
        if (rawBytes is {Length: > 0}) return true;
#elif NETCOREAPP // On .NET Core, GetPEReader is a non-public method of System.Reflection.Module, which can be used to get PEReader of assembly, and then get raw bytes from PEReader.
        var module = assembly.Modules.FirstOrDefault();
        s_GetAssemblyRawBytes ??= module?.GetType().GetMethod("GetPEReader", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (s_GetAssemblyRawBytes != null)
        {
            using var peReader = (System.Reflection.PortableExecutable.PEReader?) s_GetAssemblyRawBytes.Invoke(module, null);
            if (peReader is {HasMetadata: true})
            {
                unsafe
                {
                    var peImage = peReader.GetEntireImage();
                    if (peImage.Length > 0)
                    {
                        rawBytes = new byte[peImage.Length];
                        System.Runtime.InteropServices.Marshal.Copy((IntPtr) peImage.Pointer, rawBytes, 0, peImage.Length);
                        return true;
                    }
                }
            }
        }
#endif
        // Get raw data from assembly location.
        var location = assembly.Location;
        if (string.IsNullOrEmpty(location)) return false;
        rawBytes = File.ReadAllBytes(location);
        return true;
    }
}