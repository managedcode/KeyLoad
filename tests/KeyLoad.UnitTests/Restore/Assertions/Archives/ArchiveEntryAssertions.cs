using System.Text.Json;
using KeyLoad.Query;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Messaging;
using P = KeyLoad.UnitTests.Features.BackupRestore.ArchiveEntryProtocol;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class ArchiveEntryAssertions
{
    internal static async Task<Dictionary<string, byte[]>> CaptureAsync(string directory, CancellationToken token)
    {
        await Assert.That(Directory.GetFileSystemEntries(directory).Select(Path.GetFileName).Order(StringComparer.Ordinal)
            .SequenceEqual(P.Files.Order(StringComparer.Ordinal))).IsTrue();
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var file in P.Files)
        {
            result.Add(file, await File.ReadAllBytesAsync(Path.Combine(directory, file), token));
        }
        return result;
    }

    internal static async Task ArchiveAsync(string directory, Dictionary<string, byte[]> original, CancellationToken token)
    {
        var actual = await CaptureAsync(directory, token);
        foreach (var file in P.Files)
        {
            await Assert.That(actual[file].AsSpan().SequenceEqual(original[file])).IsTrue();
        }
    }

    internal static async Task ReceiptAsync(CommitReceipt actual, CommandRequest command,
        AtomicPartitionPlacementResolution placement, long position, long revision, DurabilityProfile durability)
    {
        var expected = new CommitReceipt(command.CommandId, new(placement.Incarnation, command.Partition.AtomicPartitionId, position), [new(P.Put, P.Collection, P.Document, revision)], durability, (durability == DurabilityProfile.Buffered ? 0 : 1));
        await Assert.That(JsonSerializer.Serialize(actual, JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(expected, JsonDefaults.Options));
        await Assert.That(actual.Mutations.All(mutation => mutation.CompositionReferences.IsEmpty)).IsTrue();
    }

    internal static async Task ImageAsync(ZoneTreeStore store, string[] rows, long position)
    {
        await Assert.That(store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(store).SequenceEqual(rows, StringComparer.Ordinal)).IsTrue();
    }

    internal static async Task ContentAsync(DatabaseEngine database, TestDatabase source, bool healthy, CancellationToken token)
    {
        var partition = source.Partition;
        var reference = source.ExpectedReference(new(partition, P.Collection, P.Document));
        var document = database.GetDocument(P.Root, reference, cancellationToken: token);
        var expected = new DocumentResult(reference, healthy ? P.HealthyRevision : P.InitialRevision,
            healthy ? P.After : P.Before, false, []);
        await Assert.That(JsonSerializer.Serialize(document, JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(expected, JsonDefaults.Options));
        var query = new QueryEngine(database, UnitExecutionOptions.QueryExecution());
        foreach (var sql in new[] { P.BeforeSql, P.AfterSql })
        {
            var page = query.Execute(P.Root, new QueryRequest(partition, sql), cancellationToken: token);
            var expectedIds = (sql == P.AfterSql) == healthy ? new[] { P.Document } : [];
            await Assert.That(page.AccessPath).IsEqualTo(P.IndexAccess);
            await Assert.That(page.Rows.Select(row => row.EntityId).SequenceEqual(expectedIds, StringComparer.Ordinal)).IsTrue();
        }
    }
}
