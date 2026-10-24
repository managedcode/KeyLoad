using System.Collections.Immutable;

namespace KeyLoad.UnitTests.ResourceExecution;

internal static class AtomicTriadStagedRecipe
{
    internal static CommandRequest Command(PartitionRef partition, bool duplicate)
        => new(Guid.NewGuid(), partition,
            [new PutDocument(AtomicTriadStagedProtocol.Collection, AtomicTriadStagedProtocol.Entity, AtomicTriadStagedProtocol.FirstJson, 0L),
             new AppendEvents(AtomicTriadStagedProtocol.Streams, AtomicTriadStagedProtocol.Entity,
                 [new(AtomicTriadStagedProtocol.FirstEvent, AtomicTriadStagedProtocol.Created, AtomicTriadStagedProtocol.FirstJson, AtomicTriadStagedProtocol.Headers)], ExpectedStreamRevision.NoStream),
             new EnqueueMessage(AtomicTriadStagedProtocol.Queue, AtomicTriadStagedProtocol.FirstMessage, AtomicTriadStagedProtocol.FirstJson, AtomicTriadStagedProtocol.Headers),
             new PutDocument(AtomicTriadStagedProtocol.Collection, AtomicTriadStagedProtocol.Entity, AtomicTriadStagedProtocol.SecondJson, 1L),
             new AppendEvents(AtomicTriadStagedProtocol.Streams, AtomicTriadStagedProtocol.Entity,
                 [new(AtomicTriadStagedProtocol.SecondEvent, AtomicTriadStagedProtocol.Replaced, AtomicTriadStagedProtocol.SecondJson, AtomicTriadStagedProtocol.Headers)], ExpectedStreamRevision.Exact(1L)),
             new EnqueueMessage(AtomicTriadStagedProtocol.Queue, duplicate ? AtomicTriadStagedProtocol.FirstMessage : AtomicTriadStagedProtocol.SecondMessage,
                 AtomicTriadStagedProtocol.SecondJson, AtomicTriadStagedProtocol.Headers)]);

    internal static ImmutableArray<MutationReceipt> Receipts =>
        [new("putDocument", AtomicTriadStagedProtocol.Collection, AtomicTriadStagedProtocol.Entity, 1L),
         new("appendEvents", AtomicTriadStagedProtocol.Streams, AtomicTriadStagedProtocol.Entity, 1L),
         new("enqueue", AtomicTriadStagedProtocol.Queue, AtomicTriadStagedProtocol.FirstMessage, 1L),
         new("putDocument", AtomicTriadStagedProtocol.Collection, AtomicTriadStagedProtocol.Entity, 2L),
         new("appendEvents", AtomicTriadStagedProtocol.Streams, AtomicTriadStagedProtocol.Entity, 2L),
         new("enqueue", AtomicTriadStagedProtocol.Queue, AtomicTriadStagedProtocol.SecondMessage, 1L)];

    internal static PrincipalRecord Principal(PartitionRef partition, long epoch, bool publish)
        => new(AtomicTriadStagedProtocol.Producer, partition.TenantId,
            [new(partition.DatabaseId, AtomicTriadStagedProtocol.Collection, Capability.DocumentsRead | Capability.DocumentsWrite | Capability.Query),
             new(partition.DatabaseId, AtomicTriadStagedProtocol.Streams, Capability.EventsRead | Capability.EventsAppend),
             new(partition.DatabaseId, AtomicTriadStagedProtocol.Queue, Capability.QueueInspect | (publish ? Capability.QueuePublish : Capability.None))], [])
        {
            PolicyEpoch = epoch
        };
}
