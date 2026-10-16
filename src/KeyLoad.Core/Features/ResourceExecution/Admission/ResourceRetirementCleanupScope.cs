using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>Allows only exact proven table deletions and this original phase's canonical metadata writes.</summary>
internal sealed class ResourceRetirementCleanupScope
{
    internal const string CandidateUnsupported = "The native cleanup commit candidate cannot be validated.";
    private const int FamilyField = 0;
    private const int MissingFamily = -1;
    private readonly ResourceLifetimeV1 lifetime;
    private readonly PartitionRef partition;
    private readonly byte[][] metadataKeys;

    internal ResourceRetirementCleanupScope(ResourceRetirementCleanupPageBodyV1 body,
        ReplicatedOperation operation, bool replicated)
    {
        lifetime = body.SealedState.Control.Lifetime;
        partition = body.Partition;
        var keys = new List<byte[]>
        {
            ResourceRetirementCleanupStorage.Key(body.SealedState.Control, body.Receiver, partition),
            KeySpace.GlobalOutcome(operation.PrincipalId, operation.Id), KeySpace.ClockBytes
        };
        if (replicated)
        {
            keys.Add(KeySpace.AppliedBytes);
        }

        metadataKeys = [.. keys];
    }

    internal void RequireMutation(byte[] key, bool hasValue)
    {
        if (metadataKeys.Any(expected => expected.AsSpan().SequenceEqual(key)))
        {
            if (!hasValue)
            {
                throw ResourceOwnerInventoryValidation.Invalid();
            }

            return;
        }
        if (hasValue)
        {
            throw ResourceOwnerInventoryValidation.Invalid();
        }

        var components = KeyCodec.Decode(key);
        if (components.Length <= FamilyField)
        {
            throw ResourceOwnerInventoryValidation.Invalid();
        }

        var family = components[FamilyField] as string;
        var ordinal = family is null ? MissingFamily : ResourceRetirementTableCleanupFamilies.All.IndexOf(family);
        if (ordinal < ResourceRetirementTableCleanupFamilies.Empty)
        {
            throw ResourceOwnerInventoryValidation.Invalid();
        }
        var prefix = ResourceRetirementTableCleanupKeys.Prefix(lifetime, partition, new(ordinal));
        ResourceRetirementTableCleanupKeys.Require(key, prefix, family!);
    }
}
