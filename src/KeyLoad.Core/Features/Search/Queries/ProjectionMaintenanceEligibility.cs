using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    /// <summary>Reads the current persisted native maintenance authority on one bounded view.</summary>
    /// <param name="consumer">The exact configured consumer scope.</param>
    /// <param name="generation">The immutable generation being dispatched.</param>
    /// <param name="budget">The original admitted read budget.</param>
    /// <returns>The current authorized immutable consumer; no live view escapes.</returns>
    public ProjectionConsumerInfo ReadProjectionMaintenanceEligibility(ProjectionConsumerRef consumer,
        long generation, ReadExecutionBudget budget)
    {
        ArgumentNullException.ThrowIfNull(consumer);
        ArgumentNullException.ThrowIfNull(budget);
        ValidatePartition(consumer.Partition);
        JsonData.Identifier(consumer.Name);
        budget.Check();
        return Store.Read(view => ReadProjectionMaintenanceEligibility(view, consumer, generation, budget));
    }

    internal ProjectionConsumerInfo ReadProjectionMaintenanceEligibilityBound(ProjectionConsumerRef consumer,
        long generation, ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        budget.AttachResourceLifetimeBinding(binding);
        ValidatePartition(consumer.Partition);
        JsonData.Identifier(consumer.Name);
        budget.Check();
        return Store.Read(view => ReadProjectionMaintenanceEligibilityCore(view, consumer, generation, budget, null, true));
    }

    internal ProjectionConsumerInfo ReadProjectionMaintenanceEligibility(global::KeyLoad.Storage.IKeyValueView view,
        ProjectionConsumerRef consumer, long generation, ReadExecutionBudget budget)
        => ReadProjectionMaintenanceEligibilityCore(view, consumer, generation, budget, null);

    private ProjectionConsumerInfo ReadProjectionMaintenanceEligibilityCore(global::KeyLoad.Storage.IKeyValueView view,
        ProjectionConsumerRef consumer, long generation, ReadExecutionBudget budget,
        ResourceDefinition? authorizedRemoteMetadata, bool bindResource = false)
    {
        var bounded = budget.CreateView(view);
        var state = ProjectionConsumer(bounded, consumer);
        var authority = state.MaintenanceAuthority
            ?? throw Errors.Fail(ErrorCode.TokenInvalidated, InvalidMaintenanceAuthority);
        var profile = state.Definition.AnnMaintenance
            ?? throw Errors.Fail(ErrorCode.TokenInvalidated, InvalidMaintenanceProfile);
        var principal = Principal(bounded, authority.PrincipalId, EvaluationClock.GetUtcNow());
        _ = RequireActiveProjectionConsumer(bounded, principal, consumer, generation);
        if (authority.ConfigureCommandId == Guid.Empty || authority.Incarnation != Store.Identity.Incarnation
            || authority.PolicyEpoch != principal.PolicyEpoch || authority.PrincipalId != principal.Id)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, InvalidMaintenanceAuthority);
        }
        RequireProjectionMaintenanceProfile(state.Definition, profile);
        Authorization.Require(principal, consumer.Partition, profile.Collection, Capability.VectorSearch);
        var resource = authorizedRemoteMetadata ?? (bindResource
            ? BindAuthorizedReadResource(bounded, consumer.Partition, profile.Collection, ResourceKind.Collection)
            : Resource(bounded, consumer.Partition, profile.Collection, ResourceKind.Collection));
        Authorization.RequireFieldUse(principal, resource, profile.Field);
        RequireProjectionMaintenanceResource(resource, profile);
        budget.Check();
        return state;
    }
}
