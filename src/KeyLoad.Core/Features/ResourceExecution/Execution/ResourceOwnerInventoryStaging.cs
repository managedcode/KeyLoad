using System.Collections.Immutable;
using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Core.Features.ResourceExecution;

internal sealed class ResourceOwnerInventoryStaging(IAtomicTransaction inner, IOptions<DatabaseLimits> options)
{
    private readonly DatabaseLimits limits = options.Value;
    private readonly HashSet<PartitionRef> candidates = [];
    private readonly Dictionary<PartitionRef, ResourceLifetimeCoverageV1> controlled = [];
    private long retainedBytes;
    private PartitionRef? imagePartition;

    internal void Observe(byte[] key, PartitionRef partition)
    {
        if (partition == imagePartition)
        {
            return;
        }
        var components = KeyCodec.Decode(key);
        if (components[ResourceOwnerInventoryProtocol.FirstField] is not string family
            || !PartitionRecordFamilies.All.Contains(family, StringComparer.Ordinal)
            || family is PartitionRecordFamilies.ResourceOwnerInventoryMember
                or PartitionRecordFamilies.ResourceOwnerInventoryProof
                or PartitionRecordFamilies.OutcomeV2 or PartitionRecordFamilies.OutcomeLocator
                or PartitionRecordFamilies.OutcomeLocatorV2 || candidates.Contains(partition))
        {
            return;
        }
        Reserve(key.Length);
        candidates.Add(partition);
    }

    internal void UseVerifiedImage(PartitionRef partition)
    {
        if (imagePartition is not null && imagePartition != partition
            || candidates.Contains(partition) || controlled.ContainsKey(partition))
        {
            throw ResourceOwnerInventoryValidation.Invalid();
        }
        imagePartition = partition;
    }

    internal void UseControlled(PartitionRef partition, ImmutableArray<ResourceDefinition> definitions,
        ResourceLifetimeCoverageV1 coverage)
    {
        PartitionControlLifetimeCoverage.Require(coverage, partition, definitions, limits.MaxBatchBytes);
        if (controlled.TryGetValue(partition, out var previous))
        {
            if (!NativeSerialization.Serialize(previous).AsSpan().SequenceEqual(NativeSerialization.Serialize(coverage)))
            {
                throw ResourceOwnerInventoryValidation.Invalid();
            }
            return;
        }
        Reserve(NativeSerialization.Measure(coverage));
        controlled.Add(partition, coverage);
    }

    internal void Reset()
    {
        candidates.Clear();
        imagePartition = null;
        controlled.Clear();
        retainedBytes = ResourceOwnerInventoryProtocol.Zero;
    }

    internal void Persist(Guid currentIncarnation)
    {
        var origin = ResourceOwnerInventoryCatalog.ReadOrigin(inner, limits);
        if (origin is null || !origin.Proven || origin.StoreIncarnation != currentIncarnation)
        {
            return;
        }
        foreach (var partition in candidates)
        {
            PersistPartition(partition, currentIncarnation);
            if (ResourceOwnerInventoryCatalog.ReadOrigin(inner, limits)?.Proven != true)
            {
                return;
            }
        }
    }

    private void PersistPartition(PartitionRef partition, Guid currentIncarnation)
    {
        controlled.TryGetValue(partition, out var supplied);
        var revision = supplied is null ? ResourceOwnerInventoryCreation.ReadRevision(inner, partition)
            : ResourceOwnerInventoryProtocol.Zero;
        var digest = supplied is null ? string.Empty
            : Convert.ToHexStringLower(SHA256.HashData(NativeSerialization.Serialize(supplied)));
        var witness = new ResourceOwnerInventoryDomainWitnessV1(currentIncarnation, revision, digest);
        var witnessKey = ResourceOwnerInventoryKeys.Witness(partition);
        var existing = inner.ReadOwnedValue(witnessKey);
        if (existing is not null
            && ResourceOwnerInventoryValidation.Decode<ResourceOwnerInventoryDomainWitnessV1>(existing, limits) == witness)
        {
            return;
        }
        var coverage = supplied ?? ResourceOwnerInventoryDomainCapture.Capture(inner, partition, limits);
        if (coverage is null)
        {
            return;
        }
        foreach (var lifetime in coverage.Lifetimes)
        {
            ResourceOwnerInventoryMutations.Enroll(inner, lifetime, partition, limits);
        }
        inner.Put(witnessKey, ResourceOwnerInventoryValidation.Encode(witness, limits));
    }

    private void Reserve(long bytes)
    {
        if (candidates.Count + controlled.Count >= limits.MaxBatchMutations
            || bytes > limits.MaxBatchBytes - retainedBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, ResourceOwnerInventoryProtocol.Capacity);
        }
        retainedBytes += bytes;
    }
}
