using KeyLoad.Server.Search;
using static KeyLoad.FunctionalTests.Search.TextProviderAuditProtocol;

namespace KeyLoad.FunctionalTests.Search;

internal static class TextProviderAuditManifestDamage
{
    internal static byte[] Encode(byte[] original, TextProviderAuditDamage damage)
    {
        var manifest = TextEnvelopeCodec.Decode<TextManifest>(original);
        var changed = damage switch
        {
            TextProviderAuditDamage.Tokenizer => manifest with
            {
                TokenizerVersion = WrongVersion
            },
            TextProviderAuditDamage.Hash => manifest with
            {
                HashVersion = WrongVersion
            },
            TextProviderAuditDamage.SourceIncarnation => manifest with
            {
                Scope = manifest.Scope with
                {
                    Incarnation = Guid.Empty
                }
            },
            TextProviderAuditDamage.ScopePosition => manifest with
            {
                Scope = manifest.Scope with
                {
                    Position = checked(manifest.Scope.Position + ScopePositionStep)
                }
            },
            TextProviderAuditDamage.RecordDigest => manifest with
            {
                RecordDigest = []
            },
            TextProviderAuditDamage.DocumentCount => manifest with
            {
                PostingProof = manifest.PostingProof with
                {
                    DocumentCount = InvalidDocumentCount
                }
            },
            TextProviderAuditDamage.ForeignPartition => ChangePartition(manifest),
            _ => ChangeFiles(manifest)
        };
        return TextEnvelopeCodec.Encode(changed);
    }

    private static TextManifest ChangePartition(TextManifest manifest)
    {
        var partition = manifest.Scope.Partition;
        return manifest with
        {
            Scope = manifest.Scope with
            {
                Partition = new(partition.TenantId, partition.DatabaseId, ForeignDomain, partition.PartitionKey)
            }
        };
    }

    private static TextManifest ChangeFiles(TextManifest manifest)
    {
        var files = manifest.Files.ToArray();
        files[FirstFile] = files[FirstFile] with
        {
            RelativePath = files[FirstFile].RelativePath + UnownedSuffix
        };
        return manifest with
        {
            Files = files
        };
    }
}
