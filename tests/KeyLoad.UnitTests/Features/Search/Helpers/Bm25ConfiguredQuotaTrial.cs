using KeyLoad.Core.Features.Search;

namespace KeyLoad.UnitTests.Features.Search;

internal static class Bm25ConfiguredQuotaTrial
{
    internal static async Task RunAsync(bool oneByteUnder)
    {
        long capacity = 0;
        await Bm25ConfiguredQuotaFlow.RunAsync(null, async flow =>
        {
            var originals = Enumerable.Range(0, Bm25ConfiguredQuotaFlow.HeadCount).Select(index => flow.Capture(index)).ToArray();
            foreach (var original in originals)
            {
                await Bm25ConfiguredQuotaBytes.RequireStoredAsync(flow.Database, original, flow.Publish(original));
            }
            capacity = Bm25ConfiguredQuotaBytes.Retained(flow.Database);
            await Assert.That(capacity).IsEqualTo(originals.Sum(Bm25ConfiguredQuotaBytes.Candidate));
        });
        await Assert.That(capacity < new DatabaseLimits().MaxBatchBytes).IsTrue();
        var configured = checked((int)capacity - (oneByteUnder ? 1 : 0));
        await Bm25ConfiguredQuotaFlow.RunAsync(configured, flow => ExecuteAsync(flow, capacity, oneByteUnder));
    }

    private static async Task ExecuteAsync(Bm25ConfiguredQuotaFlow flow, long capacity, bool oneByteUnder)
    {
        var originals = Enumerable.Range(0, Bm25ConfiguredQuotaFlow.HeadCount).Select(index => flow.Capture(index)).ToArray();
        await Assert.That(originals.Sum(Bm25ConfiguredQuotaBytes.Candidate)).IsEqualTo(capacity);
        for (var index = 0; index < originals.Length - 1; index++)
        {
            await Bm25ConfiguredQuotaBytes.RequireStoredAsync(flow.Database, originals[index], flow.Publish(originals[index]));
        }
        if (oneByteUnder)
        {
            var before = ReplicaSubmissionOutcomeState.Read(flow.Database);
            var admitted = originals[..^1];
            var retained = Bm25ConfiguredQuotaRetainedEntries.Read(flow.Database, admitted);
            var error = Assert.ThrowsExactly<KeyLoadException>(() => flow.Capture(originals.Length - 1));
            await Assert.That(error.Code).IsEqualTo(ErrorCode.BudgetExceeded);
            await Assert.That(error.Message).IsEqualTo(Bm25CorpusProtocol.Capacity);
            await before.RequireAsync(ReplicaSubmissionOutcomeState.Read(flow.Database));
            await retained.RequireAsync(flow.Database, admitted);
            await RequireReplacementAsync(flow, originals[0], capacity, false);
            return;
        }
        var final = flow.Capture(originals.Length - 1);
        await Assert.That(Bm25ConfiguredQuotaBytes.Retained(flow.Database) + Bm25ConfiguredQuotaBytes.Candidate(final)).IsEqualTo(capacity);
        await Bm25ConfiguredQuotaBytes.RequireStoredAsync(flow.Database, final, flow.Publish(final));
        await Assert.That(Bm25ConfiguredQuotaBytes.Retained(flow.Database)).IsEqualTo(capacity);
        await RequireReplacementAsync(flow, originals[0], capacity, true);
    }

    private static async Task RequireReplacementAsync(Bm25ConfiguredQuotaFlow flow, ReplicatedOperation original, long capacity, bool wasFull)
    {
        var beforeBytes = Bm25ConfiguredQuotaBytes.Retained(flow.Database);
        var reducing = flow.Capture(0, original.Id, "needle");
        await Bm25ConfiguredQuotaBytes.RequireStoredAsync(flow.Database, reducing, flow.Publish(reducing));
        await Assert.That(Bm25ConfiguredQuotaBytes.Retained(flow.Database) < beforeBytes).IsTrue();
        var healthy = flow.Capture(0, reducing.Id);
        var result = flow.Publish(healthy);
        await Bm25ConfiguredQuotaBytes.RequireStoredAsync(flow.Database, healthy, result);
        if (wasFull)
        {
            await Assert.That(Bm25ConfiguredQuotaBytes.Retained(flow.Database)).IsEqualTo(capacity);
        }
        var before = ReplicaSubmissionOutcomeState.Read(flow.Database);
        var entry = NativeSerialization.Serialize(flow.Database.ReadRetainedEntry(healthy.Id));
        var replay = flow.Database.Database.CreateVerifiedBm25CorpusPublication("root", Bm25ConfiguredQuotaFlow.Phase(healthy), flow.Database.CreateReadWork(Bm25ConfiguredQuotaFlow.Token));
        await ReplicaSubmissionOutcomeFlow.RequireResultAsync(result, flow.Publish(replay));
        await before.RequireAsync(ReplicaSubmissionOutcomeState.Read(flow.Database));
        await ReplicaSubmissionOutcomeFlow.RequireBytesAsync(entry, NativeSerialization.Serialize(flow.Database.ReadRetainedEntry(healthy.Id)));
    }
}
