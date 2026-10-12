using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PhysicalShardPartitionMovementRf3Flow
{
    private const int FirstEntry = 0;
    private const int SecondEntry = 1;
    private const int OversizedPlan = 65;
    private const int SingleFailure = 1;
    private const int NoResults = 0;
    private const long RevisionIncrement = 1;
    private const int PlacementVersion = 1;
    internal static async Task RunAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed[] seeds,
        CancellationToken token)
    {
        var original = seeds.Select(seed => seed.FirstRequest).ToArray();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            seeds[FirstEntry].Source.MoveWholePartitionsAsync(Enumerable.Repeat(original[FirstEntry], OversizedPlan).ToArray(), token));
        Assert.ThrowsExactly<ArgumentException>(() =>
            seeds[FirstEntry].Source.MoveWholePartitionsAsync([original[FirstEntry], original[FirstEntry]], token));
        var disposed = await DisposeAfterFirstAsync(seeds, original, token).ConfigureAwait(false);
        var enumeratorCancelled = await CancelEnumeratorAfterFirstAsync(seeds, original, token).ConfigureAwait(false);
        var first = await CancelAfterFirstAsync(seeds, original, token).ConfigureAwait(false);
        await SqlRf3Protocol.EqualAsync(disposed, enumeratorCancelled);
        await SqlRf3Protocol.EqualAsync(disposed, first);
        await PhysicalShardPartitionMovementRf3Cold.RequireUnstartedAndReopenAsync(wave, seeds, original, token);
        var terminals = await ExecuteAsync(seeds, original, token).ConfigureAwait(false);
        await SqlRf3Protocol.EqualAsync(first, terminals[FirstEntry]);
        for (var index = FirstEntry; index < seeds.Length; index++)
        {
            await PhysicalShardPartitionMovementRf3Placement.RequireAsync(seeds[index], original[index],
                seeds[index].OriginalPlacement, seeds[index].Directory.ControlOwner, terminals[index],
                checked(seeds[FirstEntry].OriginalPlacement.DirectoryRevision + index + RevisionIncrement),
                checked(seeds[FirstEntry].OriginalPlacement.DirectoryRevision + original.Length), token);
        }
        await VerifyAsync(seeds, token);
        await PhysicalShardPartitionMovementRf3Cold.ReplayAsync(wave, seeds, original, terminals, token);
        await RejectChangedOriginalAsync(seeds, original, token);
        var reverse = await ReverseAsync(seeds, token).ConfigureAwait(false);
        var merged = await ExecuteAsync(seeds, reverse.Requests, token).ConfigureAwait(false);
        for (var index = FirstEntry; index < seeds.Length; index++)
        {
            var owner = seeds[index].Directory.Owners.Single(entry => entry.Owner.PhysicalShardId
                == reverse.Placements[index].PhysicalShardId).Owner;
            await PhysicalShardPartitionMovementRf3Placement.RequireAsync(seeds[index], reverse.Requests[index],
                reverse.Placements[index], owner, merged[index],
                checked(reverse.Placements[FirstEntry].DirectoryRevision + index + RevisionIncrement),
                checked(reverse.Placements[FirstEntry].DirectoryRevision + reverse.Requests.Length), token);
        }
        await VerifyAsync(seeds, token);
        await PhysicalShardPartitionMovementRf3Cold.ReplayAsync(wave, seeds, reverse.Requests, merged, token);
        await PhysicalShardPartitionMovementRf3Healthy.RequireAsync(wave, seeds, reverse.Requests, merged, token);
    }

    private static async Task<PartitionMoveResult> DisposeAfterFirstAsync(PartitionMovementPublicParentRf3Seed[] seeds,
        PartitionMoveRequest[] original, CancellationToken token)
    {
        var stream = seeds[FirstEntry].Source.MoveWholePartitionsAsync(original, token);
        await using var iterator = stream.GetAsyncEnumerator(CancellationToken.None);
        await Assert.That(await iterator.MoveNextAsync()).IsTrue();
        var first = await McpCallerAssertions.SdkSuccessAsync(iterator.Current);
        await Assert.That(first.MoveId).IsEqualTo(original[FirstEntry].MoveId);
        await Assert.That(first.Phase).IsEqualTo(PartitionMovePhase.Retired);
        return first;
    }

    private static async Task<PartitionMoveResult> CancelEnumeratorAfterFirstAsync(PartitionMovementPublicParentRf3Seed[] seeds,
        PartitionMoveRequest[] original, CancellationToken token)
    {
        using var partial = CancellationTokenSource.CreateLinkedTokenSource(token);
        var stream = seeds[FirstEntry].Source.MoveWholePartitionsAsync(original, CancellationToken.None);
        await using var iterator = stream.GetAsyncEnumerator(partial.Token);
        await Assert.That(await iterator.MoveNextAsync()).IsTrue();
        var first = await McpCallerAssertions.SdkSuccessAsync(iterator.Current);
        await Assert.That(first.MoveId).IsEqualTo(original[FirstEntry].MoveId);
        await Assert.That(first.Phase).IsEqualTo(PartitionMovePhase.Retired);
        await partial.CancelAsync();
        var error = await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => iterator.MoveNextAsync().AsTask());
        await Assert.That(error!.CancellationToken).IsEqualTo(partial.Token);
        return first;
    }

    private static async Task<PartitionMoveResult> CancelAfterFirstAsync(PartitionMovementPublicParentRf3Seed[] seeds,
        PartitionMoveRequest[] original, CancellationToken token)
    {
        using var partial = CancellationTokenSource.CreateLinkedTokenSource(token);
        var callerArray = original.ToArray();
        var stream = seeds[FirstEntry].Source.MoveWholePartitionsAsync(callerArray, partial.Token);
        callerArray[FirstEntry] = callerArray[SecondEntry];
        await using var iterator = stream.GetAsyncEnumerator(CancellationToken.None);
        await Assert.That(await iterator.MoveNextAsync()).IsTrue();
        var first = await McpCallerAssertions.SdkSuccessAsync(iterator.Current);
        await Assert.That(first.MoveId).IsEqualTo(original[FirstEntry].MoveId);
        await Assert.That(first.Phase).IsEqualTo(PartitionMovePhase.Retired);
        await partial.CancelAsync();
        var error = await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => iterator.MoveNextAsync().AsTask());
        await Assert.That(error!.CancellationToken).IsEqualTo(partial.Token);
        var untouched = await McpCallerAssertions.SdkSuccessAsync(await seeds[SecondEntry].Source.ReadAtomicPartitionPlacementAsync(
            new(PlacementVersion, seeds[SecondEntry].Partition), token).ConfigureAwait(false));
        var directory = checked(seeds[FirstEntry].OriginalPlacement.DirectoryRevision + RevisionIncrement);
        await Assert.That(first.PublishedPlacement!.Revision).IsEqualTo(directory);
        await SqlRf3Protocol.EqualAsync(seeds[SecondEntry].OriginalPlacement with
        { DirectoryRevision = directory }, untouched);
        await VerifyAsync(seeds, token);
        return first;
    }

    internal static async Task<PartitionMoveResult[]> ExecuteAsync(PartitionMovementPublicParentRf3Seed[] seeds,
        PartitionMoveRequest[] original, CancellationToken token)
    {
        var actual = new List<PartitionMoveResult>();
        await foreach (var settled in seeds[FirstEntry].Source.MoveWholePartitionsAsync(original, token).ConfigureAwait(false))
        { actual.Add(await McpCallerAssertions.SdkSuccessAsync(settled)); }
        await Assert.That(actual.Count).IsEqualTo(original.Length);
        for (var index = FirstEntry; index < original.Length; index++)
        {
            await Assert.That(actual[index].MoveId).IsEqualTo(original[index].MoveId);
            await Assert.That(actual[index].Partition).IsEqualTo(original[index].Partition);
            await Assert.That(actual[index].Phase).IsEqualTo(PartitionMovePhase.Retired);
        }
        return actual.ToArray();
    }

    private static async Task RejectChangedOriginalAsync(PartitionMovementPublicParentRf3Seed[] seeds,
        PartitionMoveRequest[] original, CancellationToken token)
    {
        var changed = original.ToArray();
        changed[FirstEntry] = changed[FirstEntry] with { ExpectedPlacementRevision = checked(changed[FirstEntry].ExpectedPlacementRevision + RevisionIncrement) };
        var count = NoResults;
        await foreach (var refusal in seeds[FirstEntry].Source.MoveWholePartitionsAsync(changed, token).ConfigureAwait(false))
        {
            count++;
            await Assert.That(refusal.IsSuccess).IsFalse();
            await Assert.That(refusal.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.Conflict));
            await Assert.That(refusal.Value).IsNull();
        }
        await Assert.That(count).IsEqualTo(SingleFailure);
        await VerifyAsync(seeds, token);
    }

    private static async Task<(PartitionMoveRequest[] Requests, AtomicPartitionPlacementResolution[] Placements)>
        ReverseAsync(PartitionMovementPublicParentRf3Seed[] seeds, CancellationToken token)
    {
        var requests = new PartitionMoveRequest[seeds.Length];
        var placements = new AtomicPartitionPlacementResolution[seeds.Length];
        for (var index = FirstEntry; index < seeds.Length; index++)
        {
            var seed = seeds[index];
            var placement = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.ReadAtomicPartitionPlacementAsync(
                new(PlacementVersion, seed.Partition), token).ConfigureAwait(false));
            placements[index] = placement;
            requests[index] = new(Guid.NewGuid(), seed.Partition, seed.Directory.ControlOwner.PhysicalShardId,
                placement.Revision, PartitionMoveMode.Transfer);
        }
        return (requests, placements);
    }

    internal static async Task VerifyAsync(PartitionMovementPublicParentRf3Seed[] seeds, CancellationToken token)
    {
        foreach (var seed in seeds)
        { await seed.VerifyAsync(token).ConfigureAwait(false); }
    }
}
