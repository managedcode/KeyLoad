using System.Collections.Immutable;

namespace KeyLoad.IntegrationTests.Messaging;

internal static class AtomicTriadStagedClusterRecipe
{
    internal static CommandRequest Command(PartitionRef partition, bool duplicate)
        => new(Guid.NewGuid(), partition,
            [new PutDocument(AtomicTriadStagedClusterProtocol.Collection, AtomicTriadStagedClusterProtocol.Entity, AtomicTriadStagedClusterProtocol.FirstJson, 0L),
             new AppendEvents(AtomicTriadStagedClusterProtocol.Streams, AtomicTriadStagedClusterProtocol.Entity,
                 [new(AtomicTriadStagedClusterProtocol.FirstEvent, AtomicTriadStagedClusterProtocol.Created, AtomicTriadStagedClusterProtocol.FirstJson, AtomicTriadStagedClusterProtocol.Headers)], ExpectedStreamRevision.NoStream),
             new EnqueueMessage(AtomicTriadStagedClusterProtocol.Queue, AtomicTriadStagedClusterProtocol.FirstMessage, AtomicTriadStagedClusterProtocol.FirstJson, AtomicTriadStagedClusterProtocol.Headers),
             new PutDocument(AtomicTriadStagedClusterProtocol.Collection, AtomicTriadStagedClusterProtocol.Entity, AtomicTriadStagedClusterProtocol.SecondJson, 1L),
             new AppendEvents(AtomicTriadStagedClusterProtocol.Streams, AtomicTriadStagedClusterProtocol.Entity,
                 [new(AtomicTriadStagedClusterProtocol.SecondEvent, AtomicTriadStagedClusterProtocol.Replaced, AtomicTriadStagedClusterProtocol.SecondJson, AtomicTriadStagedClusterProtocol.Headers)], ExpectedStreamRevision.Exact(1L)),
             new EnqueueMessage(AtomicTriadStagedClusterProtocol.Queue, duplicate ? AtomicTriadStagedClusterProtocol.FirstMessage : AtomicTriadStagedClusterProtocol.SecondMessage,
                 AtomicTriadStagedClusterProtocol.SecondJson, AtomicTriadStagedClusterProtocol.Headers)]);

    internal static ImmutableArray<MutationReceipt> Receipts =>
        [new("putDocument", AtomicTriadStagedClusterProtocol.Collection, AtomicTriadStagedClusterProtocol.Entity, 1L),
         new("appendEvents", AtomicTriadStagedClusterProtocol.Streams, AtomicTriadStagedClusterProtocol.Entity, 1L),
         new("enqueue", AtomicTriadStagedClusterProtocol.Queue, AtomicTriadStagedClusterProtocol.FirstMessage, 1L),
         new("putDocument", AtomicTriadStagedClusterProtocol.Collection, AtomicTriadStagedClusterProtocol.Entity, 2L),
         new("appendEvents", AtomicTriadStagedClusterProtocol.Streams, AtomicTriadStagedClusterProtocol.Entity, 2L),
         new("enqueue", AtomicTriadStagedClusterProtocol.Queue, AtomicTriadStagedClusterProtocol.SecondMessage, 1L)];

}
