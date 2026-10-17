using KeyLoad.Server.Features.Search;
using static KeyLoad.UnitTests.Features.Search.NativeTextProviderAuditProtocol;

namespace KeyLoad.UnitTests.Features.Search;

internal static class NativeTextProviderAuditManifestDamage
{
    internal static byte[] Encode(byte[] original, NativeTextProviderAuditDamage damage)
    {
        var manifest = NativeTextEnvelopeCodec.Decode<NativeTextManifest>(original);
        var changed = damage switch
        {
            NativeTextProviderAuditDamage.Tokenizer => manifest with
            {
                TokenizerVersion = WrongVersion
            },
            NativeTextProviderAuditDamage.Hash => manifest with
            {
                HashVersion = WrongVersion
            },
            NativeTextProviderAuditDamage.Format => manifest with
            {
                FormatVersion = InvalidManifestFormat
            },
            NativeTextProviderAuditDamage.ScopePosition => manifest with
            {
                Scope = manifest.Scope with
                {
                    Position = checked(manifest.Scope.Position + ScopePositionStep)
                }
            },
            _ => ChangeRows(manifest, damage)
        };
        return NativeTextEnvelopeCodec.Encode(changed);
    }

    private static NativeTextManifest ChangeRows(NativeTextManifest manifest, NativeTextProviderAuditDamage damage)
    {
        var records = manifest.Records.ToArray();
        if (damage == NativeTextProviderAuditDamage.RecordId)
        {
            records[FirstRecord] = records[FirstRecord] with
            {
                Id = NoncontiguousId
            };
        }
        else if (damage == NativeTextProviderAuditDamage.DuplicateReference)
        {
            records[SecondRecord] = records[SecondRecord] with
            {
                Reference = records[FirstRecord].Reference
            };
        }
        else if (damage == NativeTextProviderAuditDamage.ForeignPartition)
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
            var digest = files[FirstFile].Sha256.ToArray();
            digest[FirstDigestByte] ^= DigestDifference;
            files[FirstFile] = files[FirstFile] with
            {
                Sha256 = digest
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
