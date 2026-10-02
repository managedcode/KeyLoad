using System.Collections.Immutable;

namespace KeyLoad.Server;

/// <summary>Explicit typed bindings for the frozen projection and outbox commands.</summary>
internal static class McpProjectionCatalog
{
    internal static ImmutableArray<McpOperationDescriptor> Entries { get; } =
    [
        McpOperationFactory.Command<ConfigureProjectionConsumerRequest, ProjectionConsumerInfo>(McpToolNames.ProjectionsConfigure, McpToolRoutes.ProjectionsConfigure, OperationKind.ConfigureProjectionConsumer, static request => request.CommandId),
        McpOperationFactory.Command<CommitProjectionBatchRequest, ProjectionBatchResult>(McpToolNames.ProjectionsCommit, McpToolRoutes.ProjectionsCommit, OperationKind.CommitProjectionBatch, static request => request.CommandId),
        McpOperationFactory.Command<ReleaseProjectionConsumerRequest, ProjectionConsumerInfo>(McpToolNames.ProjectionsRelease, McpToolRoutes.ProjectionsRelease, OperationKind.ReleaseProjectionConsumer, static request => request.CommandId),
        McpOperationFactory.Command<PurgeOutboxRequest, OutboxHead>(McpToolNames.OutboxPurge, McpToolRoutes.OutboxPurge, OperationKind.PurgeOutbox, static request => request.CommandId)
    ];
}
