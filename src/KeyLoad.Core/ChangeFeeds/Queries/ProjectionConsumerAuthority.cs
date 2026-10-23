using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes ProjectionConsumerAuthority operations against the caller's database context.</summary>
public static class ProjectionConsumerAuthority
{
    internal static ProjectionConsumerInfo RequireActiveProjectionConsumer(IKeyValueView view, PrincipalRecord principal, ProjectionConsumerRef consumer, long generation)
    {
        ProjectionBatchExecution.RequireProjectionAdministrator(principal);
        var state = ProjectionConsumerIdentity.ProjectionConsumer(view, consumer);
        if (state.Released || state.Definition.IndexGeneration != generation)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ProjectionOutbox.ReleasedProjectionGenerationDetail);
        }

        return state;
    }

    internal static ProjectionConsumerInfo RequireReleasedProjectionConsumer(IKeyValueView view, PrincipalRecord principal, ProjectionConsumerRef consumer, long generation)
    {
        ProjectionBatchExecution.RequireProjectionAdministrator(principal);
        var state = ProjectionConsumerIdentity.ProjectionConsumer(view, consumer);
        if (!state.Released || state.Definition.IndexGeneration != generation)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ProjectionOutbox.ReleasedProjectionGenerationDetail);
        }

        return state;
    }
}
