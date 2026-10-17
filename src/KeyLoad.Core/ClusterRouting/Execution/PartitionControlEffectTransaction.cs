using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ClusterRouting.Execution;

internal sealed class PartitionControlEffectTransaction : PartitionControlResourceView, IAtomicTransaction, IResourceEffectCompletion
{
    private readonly IAtomicTransaction inner;
    private readonly ImmutableArray<ResourceDefinition> definitions;
    private readonly ResourceLifetimeCoverageV1 coverage;
    private readonly int maximumBytes;

    internal PartitionControlEffectTransaction(IAtomicTransaction inner, PartitionRef partition,
        ImmutableArray<ResourceDefinition> definitions, ResourceLifetimeCoverageV1 coverage, int maximumBytes)
        : base(inner, partition, definitions, coverage, maximumBytes)
    {
        this.inner = inner;
        this.definitions = definitions;
        this.coverage = coverage;
        this.maximumBytes = maximumBytes;
        if (inner is IResourceOwnerInventoryContext context)
        {
            context.UseControlledResources(partition, definitions, coverage);
        }
    }

    public void Put(byte[] key, byte[] value) => inner.Put(key, value);
    public void Delete(byte[] key) => inner.Delete(key);
    public void Reset() => inner.Reset();
    void IResourceEffectCompletion.RequireCompleteResourceEffects()
    {
        if (inner is IResourceEffectCompletion completion)
        {
            completion.RequireCompleteResourceEffects();
        }
    }
    public T Prepare<T>(Func<IAtomicTransaction, T> prepare)
    {
        ArgumentNullException.ThrowIfNull(prepare);
        return inner.Prepare(child => prepare(new PartitionControlEffectTransaction(child, Partition,
            definitions, coverage, maximumBytes)));
    }
    public void ValidateCommit() => inner.ValidateCommit();
}
