using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes ChangeFeedsQueriesProjectionConsumerAuthority operations against the caller's database context.</summary>
public static class ChangeFeedsQueriesProjectionConsumerAuthority
{
    internal static ProjectionConsumerInfo RequireActiveProjectionConsumer(IKeyValueView view, PrincipalRecord principal, ProjectionConsumerRef consumer, long generation)
    {
        ProjectionBatchExecution.RequireProjectionAdministrator(principal);
        var state = ChangeFeedsValidationProjectionConsumerIdentity.ProjectionConsumer(view, consumer);
        if (state.Released || state.Definition.IndexGeneration != generation)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ChangeFeedsExecutionProjectionOutbox.ReleasedProjectionGenerationDetail);
        }

        return state;
    }

    internal static ProjectionConsumerInfo RequireReleasedProjectionConsumer(IKeyValueView view, PrincipalRecord principal, ProjectionConsumerRef consumer, long generation)
    {
        ProjectionBatchExecution.RequireProjectionAdministrator(principal);
        var state = ChangeFeedsValidationProjectionConsumerIdentity.ProjectionConsumer(view, consumer);
        if (!state.Released || state.Definition.IndexGeneration != generation)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ChangeFeedsExecutionProjectionOutbox.ReleasedProjectionGenerationDetail);
        }

        return state;
    }
}
