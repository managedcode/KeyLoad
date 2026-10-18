using System.Globalization;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;

namespace KeyLoad.AppHost.Features.ClusterReplication;

/// <summary>Reads the original native MSBuild compilation metadata from an observed managed assembly.</summary>
internal static class CompiledSourceEvidence
{
    private const string MetadataNamespace = "System.Reflection";
    private const string MetadataAttribute = "AssemblyMetadataAttribute";
    private const string Prefix = "KeyLoad.FunctionalCompileIdentity.";
    private const string Source = Prefix + "Source";
    private const string Central = Prefix + "Central";
    private const string Producer = Prefix + "Producer";
    private const string SourceCount = Prefix + "SourceCount";
    private const int CustomAttributeProlog = 1;
    private const int MinimumSources = 1;
    private const int MaximumSources = 5_000;

    internal static (int Count, string Sha256) Read(string assembly)
    {
        using var stream = File.OpenRead(assembly);
        using var portable = new PEReader(stream, PEStreamOptions.PrefetchMetadata);
        var reader = portable.GetMetadataReader();
        var inputs = new List<string>();
        var count = 0;
        foreach (var handle in reader.GetAssemblyDefinition().GetCustomAttributes())
        {
            var attribute = reader.GetCustomAttribute(handle);
            if (!IsMetadata(reader, attribute.Constructor))
            {
                continue;
            }
            var value = reader.GetBlobReader(attribute.Value);
            if (value.ReadUInt16() != CustomAttributeProlog)
            {
                throw new InvalidDataException("The compiled source attribute is invalid.");
            }
            var key = value.ReadSerializedString();
            var text = value.ReadSerializedString();
            if (key is Source or Central or Producer && text is not null)
            {
                inputs.Add(key + "=" + text);
            }
            if (key == SourceCount)
            {
                count = int.Parse(text!, NumberStyles.None, CultureInfo.InvariantCulture);
            }
        }
        if (count is < MinimumSources or > MaximumSources || inputs.Count(value => value.StartsWith(Source + "=", StringComparison.Ordinal)) != count)
        {
            throw new InvalidDataException("The observed managed assembly has no complete compilation source identity.");
        }
        var canonical = string.Join('\n', inputs.Order(StringComparer.Ordinal));
        return (count, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))));
    }

    private static bool IsMetadata(MetadataReader reader, EntityHandle constructor)
    {
        if (constructor.Kind != HandleKind.MemberReference)
        {
            return false;
        }
        var parent = reader.GetMemberReference((MemberReferenceHandle)constructor).Parent;
        if (parent.Kind != HandleKind.TypeReference)
        {
            return false;
        }
        var type = reader.GetTypeReference((TypeReferenceHandle)parent);
        return reader.StringComparer.Equals(type.Namespace, MetadataNamespace)
            && reader.StringComparer.Equals(type.Name, MetadataAttribute);
    }
}
