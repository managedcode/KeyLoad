using KeyLoad.Server;
using KeyLoad.Storage;

namespace KeyLoad.FunctionalTests.StorageRecovery;

internal sealed record PartitionStorageImage(StoreIdentity Identity, long Position, KeyValueRecord[] Rows)
{
    private const string Incomplete = "The bounded execution partial-start image is incomplete.";

    internal static PartitionStorageImage Capture(PartitionHost host, IKeyValueView view,
        CancellationToken token)
    {
        var rows = new List<KeyValueRecord>();
        var work = new ReadExecutionBudget(host.Database.EvaluationClock, token);
        var scan = view.VisitRange([], view.MaximumRangeRecords, (key, value) =>
        {
            rows.Add(new(key.ToArray(), value.ToArray()));
            return true;
        }, observer: work.ChargeBytes, cancellationToken: token);
        work.Check();
        if (scan.HasMore || scan.StoppedByVisitor)
        {
            throw new InvalidOperationException(Incomplete);
        }
        return new(host.Database.Store.Identity, host.Database.Store.Position, rows.ToArray());
    }

    internal static async Task RequireAsync(PartitionStorageImage expected, PartitionStorageImage actual)
    {
        await Assert.That(actual.Position).IsEqualTo(expected.Position);
        await Assert.That(JsonDefaults.Serialize(actual.Identity).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(expected.Identity))).IsTrue();
        await Assert.That(actual.Rows.Length).IsEqualTo(expected.Rows.Length);
        foreach (var pair in actual.Rows.Zip(expected.Rows))
        {
            await Assert.That(pair.First.Key.Span.SequenceEqual(pair.Second.Key.Span)).IsTrue();
            await Assert.That(pair.First.Value.Span.SequenceEqual(pair.Second.Value.Span)).IsTrue();
        }
    }
}
