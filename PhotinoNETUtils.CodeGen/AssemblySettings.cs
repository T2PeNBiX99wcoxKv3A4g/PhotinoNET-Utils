using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace PhotinoNetUtils.CodeGen;

/// <summary>
///     Reads the settings out of a compiled assembly instead of its source text. The metadata has
///     already resolved <c>#if</c>, partial files, <c>const</c> references and <c>nameof</c>, so what
///     it returns is exactly what the running app sees; there is no syntax left to interpret.
/// </summary>
internal static class AssemblySettings
{
    private const string SettingsNamespace = "PhotinoNetUtils.Settings";
    private const string CompilerServices = "System.Runtime.CompilerServices";
    private const string ModelAttribute = "SettingsModelAttribute";
    private const string SettingAttribute = "SettingAttribute";

    public static SettingsFile Read(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();

        var properties = new List<SettingProperty>();
        var errors = new List<string>();
        var keys = new Dictionary<string, string>(StringComparer.Ordinal);
        string? model = null;

        foreach (var handle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(handle);

            if (!type.GetDeclaringType().IsNil || Find(reader, type.GetCustomAttributes(), ModelAttribute) is null)
                continue;

            var name = reader.GetString(type.Name);

            if (model is not null && model != name)
                errors.Add($"the assembly declares more than one settings model ('{model}' and '{name}').");

            model ??= name;

            foreach (var propertyHandle in type.GetProperties())
            {
                var property = reader.GetPropertyDefinition(propertyHandle);
                var setting = Find(reader, property.GetCustomAttributes(), SettingAttribute);
                var signature = property.DecodeSignature(TypeNames.Instance, null);

                // A static, non-public or indexer member is never serialized, so it is not a
                // setting even when it carries the attribute.
                if (setting is not { } attribute || !IsSetting(reader, property, signature.ParameterTypes.Length > 0))
                    continue;

                var member = reader.GetString(property.Name);
                var args = Arguments(attribute);
                var key = args.Key ?? CamelCase(member);

                if (keys.TryGetValue(key, out var first))
                {
                    errors.Add($"duplicate setting key '{key}' from '{member}' and '{first}'; rename one of them.");
                    continue;
                }

                keys[key] = member;

                // A nullable reference type is an annotation, not a signature: the '?' only exists
                // in the NullableAttribute the compiler emits.
                var declared = signature.ReturnType.EndsWith('?') || !IsNullable(reader, type, property)
                    ? signature.ReturnType
                    : signature.ReturnType + "?";

                properties.Add(new(key, declared, args.Comment, args.Min, args.Max));
            }
        }

        return new(model, properties, errors);
    }

    private static bool IsSetting(MetadataReader reader, PropertyDefinition property, bool indexed)
    {
        // An indexer has parameters; the settings models are flat.
        if (indexed || property.GetAccessors().Getter.IsNil)
            return false;

        var accessors = property.GetAccessors();
        var getter = reader.GetMethodDefinition(accessors.Getter);
        if (getter.Attributes.HasFlag(MethodAttributes.Static))
            return false;

        return getter.Attributes.HasFlag(MethodAttributes.Public) ||
               !accessors.Setter.IsNil && reader.GetMethodDefinition(accessors.Setter).Attributes.HasFlag(MethodAttributes.Public);
    }

    private static AttributeArguments Arguments(CustomAttribute attribute)
    {
        var value = attribute.DecodeValue(AttributeTypes.Instance);
        string? comment = value.FixedArguments is [{ Value: string fixedComment }, ..] ? fixedComment : null;
        string? key = null;
        double? min = null;
        double? max = null;

        foreach (var argument in value.NamedArguments)
            switch (argument.Name)
            {
                case "Comment":
                    comment = argument.Value as string;
                    break;
                case "Key":
                    key = argument.Value as string;
                    break;
                case "Min":
                    min = argument.Value as double?;
                    break;
                case "Max":
                    max = argument.Value as double?;
                    break;
            }

        return new(comment, key, min, max);
    }

    private static CustomAttribute? Find(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes,
        string name)
    {
        foreach (var handle in attributes)
        {
            var attribute = reader.GetCustomAttribute(handle);

            if (IsAttribute(reader, attribute, name))
                return attribute;
        }

        return null;
    }

    private static bool IsAttribute(MetadataReader reader, CustomAttribute attribute, string name,
        string space = SettingsNamespace)
    {
        switch (attribute.Constructor.Kind)
        {
            case HandleKind.MemberReference:
                var member = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
                if (member.Parent.Kind != HandleKind.TypeReference)
                    return false;

                var reference = reader.GetTypeReference((TypeReferenceHandle)member.Parent);
                return reader.StringComparer.Equals(reference.Name, name) &&
                       reader.StringComparer.Equals(reference.Namespace, space);

            case HandleKind.MethodDefinition:
                var method = reader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor);
                var type = reader.GetTypeDefinition(method.GetDeclaringType());
                return reader.StringComparer.Equals(type.Name, name) &&
                       reader.StringComparer.Equals(type.Namespace, space);

            default:
                return false;
        }
    }

    /// <summary>
    ///     Whether the property was declared with <c>?</c>. The annotation lives in
    ///     <c>[Nullable]</c>, or in the <c>[NullableContext]</c> of the property or its type.
    /// </summary>
    private static bool IsNullable(MetadataReader reader, TypeDefinition owner, PropertyDefinition property)
    {
        foreach (var handle in property.GetCustomAttributes())
        {
            var attribute = reader.GetCustomAttribute(handle);

            if (IsAttribute(reader, attribute, "NullableAttribute", CompilerServices))
                return Flag(reader, attribute) == 2;
        }

        return Context(reader, property.GetCustomAttributes())
               ?? Context(reader, owner.GetCustomAttributes())
               ?? false;
    }

    private static bool? Context(MetadataReader reader, CustomAttributeHandleCollection attributes)
    {
        foreach (var handle in attributes)
        {
            var attribute = reader.GetCustomAttribute(handle);

            if (IsAttribute(reader, attribute, "NullableContextAttribute", CompilerServices))
                return Flag(reader, attribute) == 2;
        }

        return null;
    }

    /// <summary>The first nullable flag of an attribute: 1 is not nullable, 2 is nullable.</summary>
    private static byte Flag(MetadataReader reader, CustomAttribute attribute)
    {
        var value = reader.GetBlobBytes(attribute.Value);

        // The prolog is 2 bytes and the named-argument count closes the blob with 2 more, so a
        // single byte argument makes 5; an array adds a 4-byte length before its flags.
        if (value.Length == 5)
            return value[2];

        return value.Length > 8 ? value[6] : (byte)0;
    }

    private static string CamelCase(string name) =>
        char.ToLowerInvariant(name[0]) + name[1..];

    /// <summary>Decodes attribute arguments; only the values matter, so the types are placeholders.</summary>
    private sealed class AttributeTypes : ICustomAttributeTypeProvider<Type?>
    {
        public static readonly AttributeTypes Instance = new();

        public Type? GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
        {
            PrimitiveTypeCode.Boolean => typeof(bool),
            PrimitiveTypeCode.Char => typeof(char),
            PrimitiveTypeCode.SByte => typeof(sbyte),
            PrimitiveTypeCode.Byte => typeof(byte),
            PrimitiveTypeCode.Int16 => typeof(short),
            PrimitiveTypeCode.UInt16 => typeof(ushort),
            PrimitiveTypeCode.Int32 => typeof(int),
            PrimitiveTypeCode.UInt32 => typeof(uint),
            PrimitiveTypeCode.Int64 => typeof(long),
            PrimitiveTypeCode.UInt64 => typeof(ulong),
            PrimitiveTypeCode.Single => typeof(float),
            PrimitiveTypeCode.Double => typeof(double),
            PrimitiveTypeCode.String => typeof(string),
            PrimitiveTypeCode.Object => typeof(object),
            _ => null
        };

        public Type? GetSystemType() => typeof(Type);

        public Type? GetSZArrayType(Type? elementType) => typeof(Array);

        public Type? GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => null;

        public Type? GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => null;

        public Type? GetTypeFromSerializedName(string name) => null;

        public PrimitiveTypeCode GetUnderlyingEnumType(Type? type) => PrimitiveTypeCode.Int32;

        public bool IsSystemType(Type? type) => type == typeof(Type);
    }

    /// <summary>Turns a property signature into the C# spelling the TypeScript mapping understands.</summary>
    private sealed class TypeNames : ISignatureTypeProvider<string, object?>
    {
        public static readonly TypeNames Instance = new();

        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
        {
            PrimitiveTypeCode.Boolean => "bool",
            PrimitiveTypeCode.Char => "char",
            PrimitiveTypeCode.SByte => "sbyte",
            PrimitiveTypeCode.Byte => "byte",
            PrimitiveTypeCode.Int16 => "short",
            PrimitiveTypeCode.UInt16 => "ushort",
            PrimitiveTypeCode.Int32 => "int",
            PrimitiveTypeCode.UInt32 => "uint",
            PrimitiveTypeCode.Int64 => "long",
            PrimitiveTypeCode.UInt64 => "ulong",
            PrimitiveTypeCode.Single => "float",
            PrimitiveTypeCode.Double => "double",
            PrimitiveTypeCode.String => "string",
            PrimitiveTypeCode.Object => "object",
            PrimitiveTypeCode.Void => "void",
            _ => typeCode.ToString()
        };

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        {
            var type = reader.GetTypeDefinition(handle);
            return Name(reader.GetString(type.Namespace), reader.GetString(type.Name));
        }

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            var type = reader.GetTypeReference(handle);
            return Name(reader.GetString(type.Namespace), reader.GetString(type.Name));
        }

        public string GetTypeFromSpecification(
            MetadataReader reader,
            object? genericContext,
            TypeSpecificationHandle handle,
            byte rawTypeKind) =>
            reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

        public string GetSZArrayType(string elementType) => elementType + "[]";

        public string GetArrayType(string elementType, ArrayShape shape) =>
            elementType + "[" + new string(',', Math.Max(shape.Rank - 1, 0)) + "]";

        public string GetByReferenceType(string elementType) => elementType;

        public string GetPointerType(string elementType) => elementType;

        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments)
        {
            var name = genericType;
            var tick = name.IndexOf('`');
            if (tick > 0)
                name = name[..tick];

            return name == "System.Nullable" && typeArguments.Length == 1
                ? typeArguments[0] + "?"
                : name + "<" + string.Join(", ", typeArguments) + ">";
        }

        public string GetGenericMethodParameter(object? genericContext, int index) => "T" + index;

        public string GetGenericTypeParameter(object? genericContext, int index) => "T" + index;

        public string GetFunctionPointerType(MethodSignature<string> signature) => "void*";

        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;

        public string GetPinnedType(string elementType) => elementType;

        private static string Name(string ns, string name) => ns.Length == 0 ? name : ns + "." + name;
    }
}

/// <summary>The values one <c>[Setting]</c> carries.</summary>
internal sealed record AttributeArguments(string? Comment, string? Key, double? Min, double? Max);

/// <summary>One property as the generated interface and schema see it.</summary>
internal sealed record SettingProperty(string Key, string Type, string? Comment, double? Min, double? Max);

/// <summary>Everything the generator found in the settings model.</summary>
internal sealed record SettingsFile(string? ModelName, List<SettingProperty> Properties, List<string> Errors);
