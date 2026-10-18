using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>Observes a real provider-owned discard-only transaction with one original read ledger.</summary>
internal sealed class ResourceEffectPreparationTransaction : ResourceEffectObservationView, IAtomicTransaction,
    IResourceLifetimeReadBindingView, IResourceOwnerInventoryContext
{
    private readonly IAtomicTransaction inner;
    private readonly ResourceEffectBindingScope scope;
    internal ReadExecutionBudget Work
    {
        get;
    }

    internal ResourceEffectPreparationTransaction(IAtomicTransaction inner, ResourceEffectBindingScope scope,
        ReadExecutionBudget work) : base(work.CreateView(inner), scope)
    {
        this.inner = inner;
        this.scope = scope;
        Work = work;
    }

    public void Put(byte[] key, byte[] value) => inner.Put(key, value);
    public void Delete(byte[] key) => inner.Delete(key);
    public void Reset() => inner.Reset();
    public void ValidateCommit() => inner.ValidateCommit();
    public T Prepare<T>(Func<IAtomicTransaction, T> prepare)
    {
        ArgumentNullException.ThrowIfNull(prepare);
        return inner.Prepare(child => prepare(new ResourceEffectPreparationTransaction(child, scope, Work)));
    }

    void IResourceLifetimeReadBindingView.BindResourceLifetime(PartitionRef partition,
        ResourceDefinition definition, ResourceLifetimeV1 lifetime)
        => Work.ObserveResourceLifetime(partition, definition, lifetime);

    void IResourceOwnerInventoryContext.UseVerifiedImage(PartitionRef partition)
    {
        if (inner is IResourceOwnerInventoryContext context)
        {
            context.UseVerifiedImage(partition);
        }
    }

    void IResourceOwnerInventoryContext.UseControlledResources(PartitionRef partition,
        ImmutableArray<ResourceDefinition> definitions, ResourceLifetimeCoverageV1 coverage)
    {
        if (inner is IResourceOwnerInventoryContext context)
        {
            context.UseControlledResources(partition, definitions, coverage);
        }
    }
}
