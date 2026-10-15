using System.Collections.Immutable;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class AtomicTriadStagedRf3Recipe
{
    internal static CommandRequest Command(PartitionRef partition, bool duplicate)
        => new(Guid.NewGuid(), partition,
            [new PutDocument(AtomicTriadStagedRf3Protocol.Collection, AtomicTriadStagedRf3Protocol.Entity, AtomicTriadStagedRf3Protocol.FirstJson, 0L),
             new AppendEvents(AtomicTriadStagedRf3Protocol.Streams, AtomicTriadStagedRf3Protocol.Entity,
                 [new(AtomicTriadStagedRf3Protocol.FirstEvent, AtomicTriadStagedRf3Protocol.Created, AtomicTriadStagedRf3Protocol.FirstJson, AtomicTriadStagedRf3Protocol.Headers)], ExpectedStreamRevision.NoStream),
             new EnqueueMessage(AtomicTriadStagedRf3Protocol.Queue, AtomicTriadStagedRf3Protocol.FirstMessage, AtomicTriadStagedRf3Protocol.FirstJson, AtomicTriadStagedRf3Protocol.Headers),
             new PutDocument(AtomicTriadStagedRf3Protocol.Collection, AtomicTriadStagedRf3Protocol.Entity, AtomicTriadStagedRf3Protocol.SecondJson, 1L),
             new AppendEvents(AtomicTriadStagedRf3Protocol.Streams, AtomicTriadStagedRf3Protocol.Entity,
                 [new(AtomicTriadStagedRf3Protocol.SecondEvent, AtomicTriadStagedRf3Protocol.Replaced, AtomicTriadStagedRf3Protocol.SecondJson, AtomicTriadStagedRf3Protocol.Headers)], ExpectedStreamRevision.Exact(1L)),
             new EnqueueMessage(AtomicTriadStagedRf3Protocol.Queue, duplicate ? AtomicTriadStagedRf3Protocol.FirstMessage : AtomicTriadStagedRf3Protocol.SecondMessage,
                 AtomicTriadStagedRf3Protocol.SecondJson, AtomicTriadStagedRf3Protocol.Headers)]);

    internal static ImmutableArray<MutationReceipt> Receipts =>
        [new("putDocument", AtomicTriadStagedRf3Protocol.Collection, AtomicTriadStagedRf3Protocol.Entity, 1L),
         new("appendEvents", AtomicTriadStagedRf3Protocol.Streams, AtomicTriadStagedRf3Protocol.Entity, 1L),
         new("enqueue", AtomicTriadStagedRf3Protocol.Queue, AtomicTriadStagedRf3Protocol.FirstMessage, 1L),
         new("putDocument", AtomicTriadStagedRf3Protocol.Collection, AtomicTriadStagedRf3Protocol.Entity, 2L),
         new("appendEvents", AtomicTriadStagedRf3Protocol.Streams, AtomicTriadStagedRf3Protocol.Entity, 2L),
         new("enqueue", AtomicTriadStagedRf3Protocol.Queue, AtomicTriadStagedRf3Protocol.SecondMessage, 1L)];

}
