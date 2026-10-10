using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal sealed class MixedEventingRetentionBackupState
{
    internal QueueLifecycleTestState Queue { get; set; } = null!;
    internal QueueLifecycleTestState RestoredQueue { get; set; } = null!;
    internal CommitInboxRequest Inbox { get; set; } = null!;
    internal CommitInboxResult InboxResult { get; set; } = null!;
    internal CommitInboxRequest HealthyInbox { get; set; } = null!;
    internal CommitInboxResult HealthyInboxResult { get; set; } = null!;
    internal QueueLaneRef InboxSource { get; set; } = null!;
    internal Delivery InboxDelivery { get; set; } = null!;
    internal CommandRequest Publication { get; set; } = null!;
    internal CommitReceipt PublicationReceipt { get; set; } = null!;
    internal CommandRequest Purge { get; set; } = null!;
    internal CommitReceipt PurgeReceipt { get; set; } = null!;
    internal DateTimeOffset PublicationAt { get; set; }
    internal string[] CutRows { get; set; } = [];
    internal byte[] ArchiveBytes { get; set; } = [];
    internal long Cut { get; set; }
    internal OutboxStatus Outbox { get; set; } = null!;
    internal EventSourceRef Topic => new(Queue.Partition, MixedEventingRetentionBackupProtocol.Topic, EventSourceKind.Topic);
    internal SubscriptionRef Pin => new(Topic, MixedEventingRetentionBackupProtocol.Group);
    internal PartitionRef[] Partitions => [Queue.Partition, InboxSource.Partition];
}
