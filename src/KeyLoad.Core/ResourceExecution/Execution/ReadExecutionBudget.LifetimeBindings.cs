using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
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

    internal void ObserveResourceLifetime(PartitionRef partition, ResourceDefinition definition, ResourceLifetimeV1 lifetime)
        => LifetimeBindings.Observe(partition, definition, lifetime);

    internal void ObserveAuthenticatedResourceLifetimes(ImmutableArray<ResourceLifetimeV1> lifetimes)
        => LifetimeBindings.ObserveAuthenticated(lifetimes);

    internal ResourceLifetimeCoverageV1 BindResourceLifetimes(DatabaseEngine database, IKeyValueView authorizedView,
        PartitionRef partition, ImmutableArray<ResourceDefinition> definitions)
        => LifetimeBindings.Capture(database, authorizedView, partition, definitions, requireBinding: true);

    // Lower direct APIs retain same-cut pairs without D4 ownership; hosted Bound entries attach first.
    internal ResourceLifetimeCoverageV1 CaptureResourceLifetimes(DatabaseEngine database, IKeyValueView authorizedView,
        PartitionRef partition, ImmutableArray<ResourceDefinition> definitions)
        => LifetimeBindings.Capture(database, authorizedView, partition, definitions, requireBinding: false);
    internal ResourceLifetimeCoverageV1 BindAuthenticatedResourceLifetimes(DatabaseEngine database,
        IKeyValueView authorizedLocalView, ResourceLifetimeCoverageV1 coverage, ImmutableArray<ResourceDefinition> definitions)
        => LifetimeBindings.CaptureAuthenticated(database, authorizedLocalView, coverage, definitions, requireBinding: true);

    internal ResourceLifetimeCoverageV1 CaptureAuthenticatedResourceLifetimes(DatabaseEngine database,
        IKeyValueView authorizedLocalView, ResourceLifetimeCoverageV1 coverage, ImmutableArray<ResourceDefinition> definitions)
        => LifetimeBindings.CaptureAuthenticated(database, authorizedLocalView, coverage, definitions, requireBinding: false);
}
