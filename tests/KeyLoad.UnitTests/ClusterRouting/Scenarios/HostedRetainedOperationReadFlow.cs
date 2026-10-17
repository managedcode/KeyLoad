using KeyLoad.Replication;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class HostedRetainedOperationReadFlow
{
    private const int First = 1;
    private const int Last = 65;
    private const int PreviousCount = 64;
    private const int FirstArrayIndex = 0;
    private const string Document = "retained-reader-";
    private const string FollowingDocument = "retained-reader-following";

    internal static async Task ReadAndReopenAsync(CancellationToken token)
    {
        using var fixture = new ReplicaAppendBoundaryFixture();
        var entries = Seed(fixture);
        var original = entries.Last().Operation!;
        var cut = ReplicaAppendBoundaryCut.Read(fixture);
        await EqualAsync(original, Read(fixture, original.Id, token));
        await cut.RequireAsync(ReplicaAppendBoundaryCut.Read(fixture));
        Assert.ThrowsExactly<InvalidOperationException>(() => Read(fixture, Guid.NewGuid(), token));
        await cut.RequireAsync(ReplicaAppendBoundaryCut.Read(fixture));
        fixture.Reopen();
        await cut.RequireAsync(ReplicaAppendBoundaryCut.Read(fixture));
        await EqualAsync(original, Read(fixture, original.Id, token));
        await cut.RequireAsync(ReplicaAppendBoundaryCut.Read(fixture));
        await FollowingAsync(fixture, token);
    }

    internal static async Task BudgetAsync(bool bytes, CancellationToken token)
    {
        using var fixture = new ReplicaAppendBoundaryFixture();
        var entries = Seed(fixture);
        var limits = fixture.Canonical.Database.Limits;
        var restricted = bytes ? limits with
        {
            MaxBatchBytes = ReplicaProtocolCodec.SerializeEntries(entries.Take(PreviousCount).ToArray()).Length
        } : limits with
        {
            MaxScanRecords = PreviousCount
        };
        restricted.Validate();
        var cut = ReplicaAppendBoundaryCut.Read(fixture);
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            RequestCqrsRetainedOperationReader.Read(fixture.Log, entries[FirstArrayIndex].Operation!.Id, restricted, token));
        await cut.RequireAsync(ReplicaAppendBoundaryCut.Read(fixture));
        await EqualAsync(entries.Last().Operation!, Read(fixture, entries.Last().Operation!.Id, token));
        await cut.RequireAsync(ReplicaAppendBoundaryCut.Read(fixture));
        await FollowingAsync(fixture, token);
    }

    internal static async Task MissingAsync(CancellationToken token)
    {
        using var fixture = new ReplicaAppendBoundaryFixture();
        var entries = Seed(fixture);
        var key = ReplicaProtocol.EntryStorageKey(Last);
        var original = fixture.Store.Read(view => view.ReadOwnedValue(key))!;
        fixture.Store.Commit((transaction, _) =>
        {
            transaction.Delete(key);
            return true;
        });
        var corrupt = ReplicaAppendBoundaryCut.Read(fixture);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => Read(fixture, entries[FirstArrayIndex].Operation!.Id, token));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await corrupt.RequireAsync(ReplicaAppendBoundaryCut.Read(fixture));
        fixture.Store.Commit((transaction, _) =>
        {
            transaction.Put(key, original);
            return true;
        });
        var repaired = ReplicaAppendBoundaryCut.Read(fixture);
        await EqualAsync(entries[FirstArrayIndex].Operation!, Read(fixture, entries[FirstArrayIndex].Operation!.Id, token));
        await EqualAsync(entries.Last().Operation!, Read(fixture, entries.Last().Operation!.Id, token));
        await repaired.RequireAsync(ReplicaAppendBoundaryCut.Read(fixture));
        await FollowingAsync(fixture, token);
    }

    internal static async Task CanceledAsync(CancellationToken token)
    {
        using var fixture = new ReplicaAppendBoundaryFixture();
        var entries = Seed(fixture);
        var original = entries.Last().Operation!;
        var cut = ReplicaAppendBoundaryCut.Read(fixture);
        await fixture.Log.ProtocolGate.WaitAsync(token);
        try
        {
            using var selected = CancellationTokenSource.CreateLinkedTokenSource(token);
            await selected.CancelAsync();
            var failure = Assert.ThrowsExactly<OperationCanceledException>(() =>
                RequestCqrsRetainedOperationReader.Read(fixture.Log, original.Id,
                    fixture.Canonical.Database.Limits, selected.Token));
            await Assert.That(failure.CancellationToken == selected.Token).IsTrue();
            await Assert.That(fixture.Log.ProtocolGate.CurrentCount).IsEqualTo(FirstArrayIndex);
            await cut.RequireAsync(ReplicaAppendBoundaryCut.Read(fixture));
        }
        finally
        {
            fixture.Log.ProtocolGate.Release();
        }
        await Assert.That(token.IsCancellationRequested).IsFalse();
        await Assert.That(fixture.Log.ProtocolGate.CurrentCount).IsEqualTo(First);
        await EqualAsync(original, Read(fixture, original.Id, token));
        await cut.RequireAsync(ReplicaAppendBoundaryCut.Read(fixture));
        await FollowingAsync(fixture, token);
    }

    private static ReplicaEntry[] Seed(ReplicaAppendBoundaryFixture fixture)
    {
        var entries = Enumerable.Range(First, Last).Select(index => fixture.Create(Document + index, index)).ToArray();
        foreach (var batch in entries.Chunk(fixture.Configuration.MaxAppendEntries))
        {
            fixture.Log.Append([.. batch]);
        }
        fixture.Log.Commit(Last);
        return entries;
    }

    private static ReplicatedOperation Read(ReplicaAppendBoundaryFixture fixture, Guid id, CancellationToken token)
        => RequestCqrsRetainedOperationReader.Read(fixture.Log, id, fixture.Canonical.Database.Limits, token);

    private static Task EqualAsync(ReplicatedOperation expected, ReplicatedOperation actual)
        => ReplicaAppendBoundaryCut.RequireBytesAsync(NativeSerialization.Serialize(expected), NativeSerialization.Serialize(actual));

    private static async Task FollowingAsync(ReplicaAppendBoundaryFixture fixture, CancellationToken token)
    {
        var following = fixture.Create(FollowingDocument, checked(fixture.Log.State.LastIndex + First));
        fixture.Log.Append([following]);
        fixture.Log.Commit(following.Index);
        var cut = ReplicaAppendBoundaryCut.Read(fixture);
        await EqualAsync(following.Operation!, Read(fixture, following.Operation!.Id, token));
        await cut.RequireAsync(ReplicaAppendBoundaryCut.Read(fixture));
    }
}
