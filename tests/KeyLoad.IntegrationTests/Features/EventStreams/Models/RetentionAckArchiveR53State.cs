using KeyLoad.IntegrationTests.Features.BackupRestore;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal sealed class RetentionAckArchiveR53State(ClusterRestoreMixedRetentionRf3State original)
{
    internal ClusterRestoreMixedRetentionRf3State Original { get; } = original;
    internal SubscriptionRef Group => new(Original.PurgedSource, RetentionAckArchiveR53Protocol.Group);
    internal SubscriptionDefinition Definition => new(Original.Principal.Id);
    internal ConfigureSubscriptionRequest Configure { get; set; } = null!;
    internal SubscriptionInfo Configured { get; set; } = null!;
    internal ReceiveSubscriptionRequest Claim { get; set; } = null!;
    internal ReceiveSubscriptionResult Claimed { get; set; } = null!;
    internal CommandRequest Refused { get; set; } = null!;
    internal ManagedCode.Communication.Result<CommitReceipt> Refusal { get; set; }
    internal List<(SubscriptionDeliveryCommand Request, CommitReceipt Receipt)> Acks { get; } = [];
    internal List<(SetSubscriptionPausedRequest Request, SubscriptionInfo Result)> Pauses { get; } = [];
    internal EventSourcePage Page { get; set; } = null!;
    internal OutboxStatus Outbox { get; set; } = null!;
    internal AtomicPartitionPlacementResolution Placement { get; set; } = null!;
    internal long Checkpoint { get; set; }
    internal long Issued { get; set; }
    internal bool Paused { get; set; }
    internal bool Retained { get; set; }
    internal SetSubscriptionPausedRequest? CurrentResume { get; set; }
    internal SubscriptionInfo? Resumed { get; set; }
    internal ReceiveSubscriptionRequest? Empty { get; set; }
    internal ReceiveSubscriptionResult? EmptyResult { get; set; }
}
