using System.Collections.Immutable;
using System.Text;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Queries;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class PartitionRecordNativeFixture : IDisposable
{
    internal const string TenantId = "tenant-a";
    internal const string DatabaseId = "database-a";
    internal const string DomainId = "domain-a";
    internal const string PartitionKey = "partition-a";
    private const string DirectoryPrefix = "keyload-partition-record-pages-";
    private readonly string directory = Path.Combine(Path.GetTempPath(),
        DirectoryPrefix + Guid.NewGuid().ToString("N"));
    private ZoneTreeStore? store;

    internal PartitionRecordNativeFixture()
    {
        var failures = new List<Exception>();
        var creationCompleted = false;
        KeyLoad.Server.ServerFailureObserver.Observe(() =>
        {
            store = new(new(directory));
            creationCompleted = true;
        }, failures);
        if (failures.Count == 0)
        {
            return;
        }

        var nativeClosed = false;
        if (creationCompleted && store is not null)
        {
            var cleanupFailures = new List<Exception>();
            KeyLoad.Server.ServerFailureObserver.Observe(store.Dispose, cleanupFailures);
            nativeClosed = cleanupFailures.Count == 0;
            failures.AddRange(cleanupFailures);
            if (nativeClosed)
            {
                store = null;
            }
        }

        if (nativeClosed && Directory.Exists(directory))
        {
            KeyLoad.Server.ServerFailureObserver.Observe(
                () => Directory.Delete(directory, recursive: true), failures);
        }

        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
    internal ZoneTreeStore Store => store ?? throw new ObjectDisposedException(nameof(PartitionRecordNativeFixture));

    internal static byte[] Key(string family, PartitionRef partition, string suffix)
        => KeySpace.Partition(family, partition, suffix);

    internal static (byte[] Key, byte[] Value) Row(string family, PartitionRef partition,
        string suffix, string value)
        => (Key(family, partition, suffix), Encoding.UTF8.GetBytes(value));

    internal static void Seed(ZoneTreeStore target, params (byte[] Key, byte[] Value)[] rows)
        => target.Commit((transaction, _) =>
        {
            foreach (var row in rows)
            { transaction.Put(row.Key, row.Value); }
            return true;
        });

    internal static PartitionRecordPage Read(ZoneTreeStore target, PartitionRef partition, string family,
        int records, long retained, long examined, ReadOnlyMemory<byte> after = default,
        CancellationToken cancellationToken = default)
        => target.Read(view => PartitionRecordPageReader.Read(view, partition, family, records,
            retained, examined, after, cancellationToken));

    internal static string Hex(ReadOnlySpan<byte> value) => Convert.ToHexString(value);

    internal static string PageKeys(ImmutableArray<KeyValueRecord> records)
        => string.Join('|', records.Select(record => Hex(record.Key.Span)));

    internal static string PageValues(ImmutableArray<KeyValueRecord> records)
        => string.Join('|', records.Select(record => Hex(record.Value.Span)));

    internal void Reopen()
    {
        Store.Dispose();
        store = null;
        store = new(new(directory));
    }

    public void Dispose()
    {
        store?.Dispose();
        store = null;
        if (Directory.Exists(directory))
        { Directory.Delete(directory, recursive: true); }
    }
}
