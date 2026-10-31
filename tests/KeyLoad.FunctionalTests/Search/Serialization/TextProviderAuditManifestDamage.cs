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
            TextProviderAuditDamage.Format => manifest with
            {
                FormatVersion = InvalidManifestFormat
            },
            TextProviderAuditDamage.ScopePosition => manifest with
            {
                Scope = manifest.Scope with
                {
                    Position = checked(manifest.Scope.Position + ScopePositionStep)
                }
            },
            _ => ChangeRows(manifest, damage)
        };
        return TextEnvelopeCodec.Encode(changed);
    }

    private static TextManifest ChangeRows(TextManifest manifest, TextProviderAuditDamage damage)
    {
        var records = manifest.Records.ToArray();
        if (damage == TextProviderAuditDamage.RecordId)
        {
            records[FirstRecord] = records[FirstRecord] with
            {
                Id = NoncontiguousId
            };
        }
        else if (damage == TextProviderAuditDamage.DuplicateReference)
        {
            records[SecondRecord] = records[SecondRecord] with
            {
                Reference = records[FirstRecord].Reference
            };
        }
        else if (damage == TextProviderAuditDamage.ForeignPartition)
        {
            var reference = records[FirstRecord].Reference;
            records[FirstRecord] = records[FirstRecord] with
            {
                Reference = reference with
                {
                    Partition = new(reference.Partition.TenantId, reference.Partition.DatabaseId,
                        ForeignDomain, reference.Partition.PartitionKey)
                }
            };
        }
        else
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
        return manifest with
        {
            Records = records
        };
    }
}
