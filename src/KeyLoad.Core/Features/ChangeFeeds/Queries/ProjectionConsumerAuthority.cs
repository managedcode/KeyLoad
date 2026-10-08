using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal static ProjectionConsumerInfo RequireActiveProjectionConsumer(IKeyValueView view,
        PrincipalRecord principal, ProjectionConsumerRef consumer, long generation)
    {
        RequireProjectionAdministrator(principal);
        var state = ProjectionConsumer(view, consumer);
        if (state.Released || state.Definition.IndexGeneration != generation)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ReleasedProjectionGenerationDetail);
        }
        return state;
    }
    internal static ProjectionConsumerInfo RequireReleasedProjectionConsumer(IKeyValueView view,
        PrincipalRecord principal, ProjectionConsumerRef consumer, long generation)
    {
        RequireProjectionAdministrator(principal);
        var state = ProjectionConsumer(view, consumer);
        if (!state.Released || state.Definition.IndexGeneration != generation)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, ReleasedProjectionGenerationDetail); }
        return state;
    }

}
