using System.Collections.Immutable;
using KeyLoad.Core.ResourceExecution;
using KeyLoad.Core.ResourceExecution.Execution;
using KeyLoad.Core.StoragePartitions.Contracts;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class ReadExecutionBudget
{
    private ReadExecutionBudgetLifetimeBindings? lifetimeBindings;
    private ReadExecutionBudgetLifetimeBindings LifetimeBindings => lifetimeBindings ??= new(this);

    internal void AttachResourceLifetimeBinding(IResourceLifetimeWorkBinding binding) => LifetimeBindings.Attach(binding);

    internal IResourceLifetimeWorkBinding RequireAttachedResourceLifetimeBinding() => LifetimeBindings.RequireAttached();

    internal void RequireResourceLifetimeBinding(IResourceLifetimeWorkBinding binding) => LifetimeBindings.Require(binding);

    internal void ObserveResourceLifetime(PartitionRef partition, ResourceDefinition definition, ResourceLifetime lifetime)
        => LifetimeBindings.Observe(partition, definition, lifetime);

    internal void ObserveAuthenticatedResourceLifetimes(ImmutableArray<ResourceLifetime> lifetimes)
        => LifetimeBindings.ObserveAuthenticated(lifetimes);

    internal ResourceLifetimeCoverage BindResourceLifetimes(IKeyValueView authorizedView,
        PartitionRef partition, ImmutableArray<ResourceDefinition> definitions)
        => LifetimeBindings.Capture(authorizedView, partition, definitions, requireBinding: true);

    // Lower direct APIs retain same-cut pairs without D4 ownership; hosted Bound entries attach first.
    internal ResourceLifetimeCoverage CaptureResourceLifetimes(IKeyValueView authorizedView,
        PartitionRef partition, ImmutableArray<ResourceDefinition> definitions)
        => LifetimeBindings.Capture(authorizedView, partition, definitions, requireBinding: false);
    internal ResourceLifetimeCoverage BindAuthenticatedResourceLifetimes(
        IKeyValueView authorizedLocalView, ResourceLifetimeCoverage coverage, ImmutableArray<ResourceDefinition> definitions)
        => LifetimeBindings.CaptureAuthenticated(authorizedLocalView, coverage, definitions, requireBinding: true);

    internal ResourceLifetimeCoverage CaptureAuthenticatedResourceLifetimes(
        IKeyValueView authorizedLocalView, ResourceLifetimeCoverage coverage, ImmutableArray<ResourceDefinition> definitions)
        => LifetimeBindings.CaptureAuthenticated(authorizedLocalView, coverage, definitions, requireBinding: false);
}
