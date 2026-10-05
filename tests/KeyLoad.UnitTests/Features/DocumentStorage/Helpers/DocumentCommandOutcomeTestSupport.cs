using System.Collections.Immutable;
using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.DocumentStorage;

internal static class DocumentCommandOutcomeTestSupport
{
    private const string Administrator = "root";
    private const string DatabaseName = "database";
    private const string Orders = "orders";
    private const int MaximumOutboxEntries = 16;

    internal static void ConfigureWriter(TestDatabase database, string principal)
    {
        var persisted = new PrincipalRecord(principal, database.Partition.TenantId,
            [new(DatabaseName, Orders, Capability.DocumentsWrite)], []);
        var configured = database.Submit(OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(persisted)).Get<PrincipalRecord>();
        if (configured.Id != principal || configured.ClusterAdministrator)
        {
            throw new InvalidOperationException("The persisted writer fixture is invalid.");
        }
    }

    internal static byte[] OutcomeBytes(TestDatabase database, string principal, Guid commandId)
        => database.Store.Read(view => view.ReadOwnedValue(KeySpace.Outcome(principal, commandId)))
            ?? throw new InvalidOperationException("The command outcome was not persisted.");

    internal static OutboxSnapshot CaptureOutbox(TestDatabase database)
    {
        var head = database.Database.GetOutboxStatus(Administrator, database.Partition).Head;
        if (head.Tail - head.FirstAvailable + 1 > MaximumOutboxEntries)
        {
            throw new InvalidOperationException("The bounded command fixture produced too many outbox entries.");
        }
        var headKey = KeySpace.Partition("outbox-head", database.Partition);
        return database.Store.Read(view =>
        {
            var entries = ImmutableArray.CreateBuilder<byte[]>(MaximumOutboxEntries);
            for (var sequence = head.FirstAvailable; sequence <= head.Tail; sequence++)
            {
                var bytes = view.ReadOwnedValue(KeySpace.Partition("outbox", database.Partition, sequence))
                    ?? throw new InvalidOperationException("A committed outbox entry is missing.");
                entries.Add(bytes);
            }
            return new OutboxSnapshot(view.ReadOwnedValue(headKey), entries.ToImmutable(), head.Tail);
        });
    }

    internal static long OutboxTail(OutboxSnapshot snapshot) => snapshot.Tail;

    internal static bool OutboxEqual(OutboxSnapshot left, OutboxSnapshot right)
    {
        if (left.Tail != right.Tail || !BytesEqual(left.HeadBytes, right.HeadBytes)
            || left.EntryBytes.Length != right.EntryBytes.Length)
        {
            return false;
        }
        for (var index = 0; index < left.EntryBytes.Length; index++)
        {
            if (!left.EntryBytes[index].AsSpan().SequenceEqual(right.EntryBytes[index]))
            {
                return false;
            }
        }
        return true;
    }

    private static bool BytesEqual(byte[]? left, byte[]? right)
        => left is null ? right is null : right is not null && left.AsSpan().SequenceEqual(right);
}

internal sealed record OutboxSnapshot(byte[]? HeadBytes, ImmutableArray<byte[]> EntryBytes, long Tail);
