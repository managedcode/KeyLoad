using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ClusterRouting.Execution;

internal sealed class PartitionControlEffectTransaction : PartitionControlResourceView, IAtomicTransaction
{
    private readonly IAtomicTransaction inner;

    internal PartitionControlEffectTransaction(IAtomicTransaction inner, PartitionRef partition,
        ImmutableArray<ResourceDefinition> definitions, ResourceLifetimeCoverageV1 coverage, int maximumBytes)
        : base(inner, partition, definitions, coverage, maximumBytes) => this.inner = inner;

    public void Put(byte[] key, byte[] value) => inner.Put(key, value);
    public void Delete(byte[] key) => inner.Delete(key);
    public void Reset() => inner.Reset();
    public void ValidateCommit() => inner.ValidateCommit();
}
