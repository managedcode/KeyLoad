using System.Collections.Immutable;
using KeyLoad.Core.Features.CommandOutcomes.Contracts;
using KeyLoad.Core.Features.RuntimeJournaling.Contracts;
using KeyLoad.Core.Features.StoragePartitions.Contracts;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Core.Features.ResourceExecution.Execution;
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

    internal ResourceLifetimeCoverage BindResourceLifetimes(DatabaseEngine database, IKeyValueView authorizedView,
        PartitionRef partition, ImmutableArray<ResourceDefinition> definitions)
        => LifetimeBindings.Capture(database, authorizedView, partition, definitions, requireBinding: true);

    // Lower direct APIs retain same-cut pairs without D4 ownership; hosted Bound entries attach first.
    internal ResourceLifetimeCoverage CaptureResourceLifetimes(DatabaseEngine database, IKeyValueView authorizedView,
        PartitionRef partition, ImmutableArray<ResourceDefinition> definitions)
        => LifetimeBindings.Capture(database, authorizedView, partition, definitions, requireBinding: false);
    internal ResourceLifetimeCoverage BindAuthenticatedResourceLifetimes(DatabaseEngine database,
        IKeyValueView authorizedLocalView, ResourceLifetimeCoverage coverage, ImmutableArray<ResourceDefinition> definitions)
        => LifetimeBindings.CaptureAuthenticated(database, authorizedLocalView, coverage, definitions, requireBinding: true);

    internal ResourceLifetimeCoverage CaptureAuthenticatedResourceLifetimes(DatabaseEngine database,
        IKeyValueView authorizedLocalView, ResourceLifetimeCoverage coverage, ImmutableArray<ResourceDefinition> definitions)
        => LifetimeBindings.CaptureAuthenticated(database, authorizedLocalView, coverage, definitions, requireBinding: false);
}
