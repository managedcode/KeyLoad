using System.Security.Cryptography;
using KeyLoad.Storage;

namespace KeyLoad.IntegrationTests.Features.Messaging;

/// <summary>Independent oracle for the frozen coordinator command ID encoding.</summary>
internal static class DueCommandRf3Identity
{
    private const string Domain = "keyload-due-command-v1";
    private const string GuidFormat = "N";

    internal static Guid Schedule(QueueLaneRef lane, Guid scheduleId)
        => Create(0L, lane, scheduleId, revision: 1, generation: 1, ordinal: 0);

    internal static Guid Saga(QueueLaneRef lane, Guid sagaId)
        => Create(1L, lane, sagaId, revision: 1, generation: 0, ordinal: 0);

    private static Guid Create(long kind, QueueLaneRef lane, Guid id, long revision, long generation, long ordinal)
    {
        var partition = lane.Partition;
        var encoded = KeyCodec.Encode(Domain, kind, partition.TenantId, partition.DatabaseId,
            partition.TransactionDomainId, partition.PartitionKey, lane.Queue, id.ToString(GuidFormat),
            revision, generation, ordinal);
        var digest = SHA256.HashData(encoded);
        var commandId = new Guid(digest.AsSpan(0, 16));
        return commandId == Guid.Empty ? new Guid(digest.AsSpan(16, 16)) : commandId;
    }
}
