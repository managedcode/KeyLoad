using KeyLoad.Orleans;

namespace KeyLoad.Server;

/// <summary>Explicit advisory behavior of each public operation, independent of authorization.</summary>
/// <param name="ReadOnly">Whether the operation lacks database or physical side effects.</param>
/// <param name="Idempotent">Whether a valid identical retry has stable effects.</param>
/// <param name="Destructive">Whether the operation can remove or consume existing data.</param>
internal readonly record struct McpToolHints(bool ReadOnly, bool Idempotent, bool Destructive)
{
    internal static McpToolHints ForRead(GrainReadKind kind) => kind switch
    {
        GrainReadKind.Document => new(true, true, false),
        GrainReadKind.Stream => new(true, true, false),
        GrainReadKind.EventSource => new(true, true, false),
        GrainReadKind.Subscription => new(true, true, false),
        GrainReadKind.Message => new(true, true, false),
        GrainReadKind.Traverse => new(true, true, false),
        GrainReadKind.Samples => new(true, true, false),
        GrainReadKind.Query => new(true, true, false),
        GrainReadKind.AstQuery => new(true, true, false),
        GrainReadKind.QueryCapabilities => new(true, true, false),
        GrainReadKind.ChangeFeed => new(true, true, false),
        GrainReadKind.LiveQueryStart => new(true, true, false),
        GrainReadKind.LiveQueryRead => new(true, true, false),
        GrainReadKind.OutboxStatus => new(true, true, false),
        GrainReadKind.ProjectionBatch => new(true, true, false),
        GrainReadKind.Search => new(true, true, false),
        GrainReadKind.Backup => new(false, false, false),
        GrainReadKind.Admission => new(true, true, false),
        GrainReadKind.NodeStatus => new(true, true, false),
        GrainReadKind.AdminDashboard or GrainReadKind.AdminResources or GrainReadKind.AdminQueue
            => new(true, true, false),
        GrainReadKind.BlobMetadata or GrainReadKind.BlobUploadInfo or GrainReadKind.BlobRange or GrainReadKind.BlobList
            => new(true, true, false),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), McpCatalogProtocol.InvalidOperation)
    };

    internal static McpToolHints ForCommand(OperationKind kind) => kind switch
    {
        OperationKind.Batch => new(false, true, true),
        OperationKind.Receive => new(false, true, true),
        OperationKind.Delivery => new(false, true, true),
        OperationKind.Processing => new(false, true, true),
        OperationKind.ConfigureResource => new(false, true, false),
        OperationKind.ConfigurePrincipal => new(false, true, true),
        OperationKind.ConfigureApiKey => new(false, true, true),
        OperationKind.SetDispatch => new(false, true, true),
        OperationKind.ConfigureSubscription => new(false, true, true),
        OperationKind.SeekSubscription => new(false, true, true),
        OperationKind.ReceiveSubscription => new(false, true, true),
        OperationKind.SubscriptionDelivery => new(false, true, true),
        OperationKind.SubscriptionProcessing => new(false, true, true),
        OperationKind.SetSubscriptionPaused => new(false, true, true),
        OperationKind.ConfigureProjectionConsumer => new(false, true, true),
        OperationKind.CommitProjectionBatch => new(false, true, true),
        OperationKind.ReleaseProjectionConsumer => new(false, true, true),
        OperationKind.PurgeOutbox => new(false, true, true),
        OperationKind.BeginBlobUpload or OperationKind.WriteBlobPart or OperationKind.AbortBlobUpload
            => new(false, true, false),
        OperationKind.CompleteBlobUpload or OperationKind.DeleteBlob or OperationKind.ReclaimBlob
            => new(false, true, true),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), McpCatalogProtocol.InvalidOperation)
    };
}
