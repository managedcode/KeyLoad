using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Core;
/// <summary>Executes SearchQueriesProjectionMaintenanceEligibility operations against the caller's database context.</summary>
public static class SearchQueriesProjectionMaintenanceEligibility
{
    /// <summary>Reads the current persisted execution maintenance authority on one bounded view.</summary>
    /// <param name = "consumer">The exact configured consumer scope.</param>
    /// <param name = "generation">The immutable generation being dispatched.</param>
    /// <param name = "budget">The original admitted read budget.</param>
    /// <returns>The current authorized immutable consumer; no live view escapes.</returns>

    /// <param name = "engine">The shared database execution context.</param>
    public static ProjectionConsumerInfo ReadProjectionMaintenanceEligibility(this DatabaseEngine engine, ProjectionConsumerRef consumer, long generation, ReadExecutionBudget budget)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(consumer);
        ArgumentNullException.ThrowIfNull(budget);
        EngineResourceAccess.ValidatePartition(consumer.Partition);
        JsonData.Identifier(consumer.Name);
        budget.Check();
        return engine.Store.Read(view => engine.ReadProjectionMaintenanceEligibility(view, consumer, generation, budget));
    }

    internal static ProjectionConsumerInfo ReadProjectionMaintenanceEligibilityBound(this DatabaseEngine engine, ProjectionConsumerRef consumer, long generation, ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        budget.AttachResourceLifetimeBinding(binding);
        EngineResourceAccess.ValidatePartition(consumer.Partition);
        JsonData.Identifier(consumer.Name);
        budget.Check();
        return engine.Store.Read(view => engine.ReadProjectionMaintenanceEligibilityCore(view, consumer, generation, budget, null, true));
    }

    internal static ProjectionConsumerInfo ReadProjectionMaintenanceEligibility(this DatabaseEngine engine, global::KeyLoad.Storage.IKeyValueView view, ProjectionConsumerRef consumer, long generation, ReadExecutionBudget budget) => engine.ReadProjectionMaintenanceEligibilityCore(view, consumer, generation, budget, null);
    internal static ProjectionConsumerInfo ReadProjectionMaintenanceEligibilityCore(this DatabaseEngine engine, global::KeyLoad.Storage.IKeyValueView view, ProjectionConsumerRef consumer, long generation, ReadExecutionBudget budget, ResourceDefinition? authorizedRemoteMetadata, bool bindResource = false)
    {
        var bounded = budget.CreateView(view);
        var state = ChangeFeedsValidationProjectionConsumerIdentity.ProjectionConsumer(bounded, consumer);
        var authority = state.MaintenanceAuthority ?? throw Errors.Fail(ErrorCode.TokenInvalidated, SearchCommandsProjectionMaintenanceConfiguration.InvalidMaintenanceAuthority);
        var profile = state.Definition.AnnMaintenance ?? throw Errors.Fail(ErrorCode.TokenInvalidated, SearchCommandsProjectionMaintenanceConfiguration.InvalidMaintenanceProfile);
        var principal = engine.Principal(bounded, authority.PrincipalId, engine.EvaluationClock.GetUtcNow());
        _ = ChangeFeedsQueriesProjectionConsumerAuthority.RequireActiveProjectionConsumer(bounded, principal, consumer, generation);
        if (authority.ConfigureCommandId == Guid.Empty || authority.Incarnation != engine.Store.Identity.Incarnation || authority.PolicyEpoch != principal.PolicyEpoch || authority.PrincipalId != principal.Id)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, SearchCommandsProjectionMaintenanceConfiguration.InvalidMaintenanceAuthority);
        }

        SearchCommandsProjectionMaintenanceConfiguration.RequireProjectionMaintenanceProfile(state.Definition, profile);
        engine.Authorization.Require(principal, consumer.Partition, profile.Collection, Capability.VectorSearch);
        var resource = authorizedRemoteMetadata ?? (bindResource ? ResourceExecutionQueriesResourceLifetimeReadBinding.BindAuthorizedReadResource(bounded, consumer.Partition, profile.Collection, ResourceKind.Collection) : engine.Resource(bounded, consumer.Partition, profile.Collection, ResourceKind.Collection));
        engine.Authorization.RequireFieldUse(principal, resource, profile.Field);
        SearchCommandsProjectionMaintenanceConfiguration.RequireProjectionMaintenanceResource(resource, profile);
        budget.Check();
        return state;
    }
}
