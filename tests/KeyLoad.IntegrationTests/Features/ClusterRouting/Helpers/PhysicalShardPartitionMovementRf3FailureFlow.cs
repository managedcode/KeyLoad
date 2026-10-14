using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PhysicalShardPartitionMovementRf3FailureFlow
{
    private const int FirstEntry = 0;
    private const int SecondEntry = 1;
    private const int ThirdEntry = 2;
    private const long RevisionIncrement = 1;
    private const int SettledBeforeRepair = 2;
    private const int PlacementVersion = 1;

    internal static async Task RunAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed[] seeds,
        CancellationToken token)
    {
        var original = seeds.Select(seed => seed.FirstRequest).ToArray();
        var second = await McpCallerAssertions.SdkSuccessAsync(await seeds[SecondEntry].Source.MovePartitionAsync(
            original[SecondEntry], token).ConfigureAwait(false));
        var first = await RequireConflictAsync(seeds, original, token).ConfigureAwait(false);
        await RequirePartialPlacementsAsync(seeds, original, first, second, token);
        var before = await PhysicalShardPartitionMovementRf3Cold.StopAndReadAsync(wave, original, token);
        await RequireStoppedAsync(before);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, token);
        await SqlRf3Protocol.EqualAsync(first, await RequireConflictAsync(seeds, original, token));
        var after = await PhysicalShardPartitionMovementRf3Cold.StopAndReadAsync(wave, original, token);
        await SqlRf3Protocol.EqualAsync(before, after);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, token);
        var terminals = await PhysicalShardPartitionMovementRf3Flow.ExecuteAsync(seeds, original, token);
        await SqlRf3Protocol.EqualAsync(first, terminals[FirstEntry]);
        await SqlRf3Protocol.EqualAsync(second, terminals[SecondEntry]);
        await RequirePlacementsAsync(seeds, original, terminals, token);
        await PhysicalShardPartitionMovementRf3Cold.ReplayAsync(wave, seeds, original, terminals, token);
        await PhysicalShardPartitionMovementRf3Flow.MergeAsync(wave, seeds, token);
    }

    private static async Task<PartitionMoveResult> RequireConflictAsync(PartitionMovementPublicParentRf3Seed[] seeds,
        PartitionMoveRequest[] original, CancellationToken token)
    {
        var changed = original.ToArray();
        changed[SecondEntry] = changed[SecondEntry] with
        {
            ExpectedPlacementRevision = checked(changed[SecondEntry].ExpectedPlacementRevision + RevisionIncrement)
        };
        await using var iterator = seeds[FirstEntry].Source.MoveWholePartitionsAsync(changed, token)
            .GetAsyncEnumerator(CancellationToken.None);
        await Assert.That(await iterator.MoveNextAsync()).IsTrue();
        var first = await McpCallerAssertions.SdkSuccessAsync(iterator.Current);
        await Assert.That(first.MoveId).IsEqualTo(original[FirstEntry].MoveId);
        await Assert.That(first.Phase).IsEqualTo(PartitionMovePhase.Retired);
        await Assert.That(await iterator.MoveNextAsync()).IsTrue();
        await Assert.That(iterator.Current.IsSuccess).IsFalse();
        await Assert.That(iterator.Current.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.Conflict));
        await Assert.That(iterator.Current.Value).IsNull();
        await Assert.That(await iterator.MoveNextAsync()).IsFalse();
        await PhysicalShardPartitionMovementRf3Flow.VerifyAsync(seeds, token);
        return first;
    }

    private static async Task RequirePartialPlacementsAsync(PartitionMovementPublicParentRf3Seed[] seeds,
        PartitionMoveRequest[] original, PartitionMoveResult first, PartitionMoveResult second, CancellationToken token)
    {
        var revision = seeds[FirstEntry].OriginalPlacement.DirectoryRevision;
        var directory = checked(revision + SettledBeforeRepair);
        await PhysicalShardPartitionMovementRf3Placement.RequireAsync(seeds[FirstEntry], original[FirstEntry],
            seeds[FirstEntry].OriginalPlacement, seeds[FirstEntry].Directory.ControlOwner, first, directory, directory, token);
        await PhysicalShardPartitionMovementRf3Placement.RequireAsync(seeds[SecondEntry], original[SecondEntry],
            seeds[SecondEntry].OriginalPlacement, seeds[SecondEntry].Directory.ControlOwner, second,
            checked(revision + RevisionIncrement), directory, token);
        var untouched = await McpCallerAssertions.SdkSuccessAsync(await seeds[ThirdEntry].Source.ReadAtomicPartitionPlacementAsync(
            new(PlacementVersion, seeds[ThirdEntry].Partition), token).ConfigureAwait(false));
        await SqlRf3Protocol.EqualAsync(seeds[ThirdEntry].OriginalPlacement with
        {
            DirectoryRevision = directory
        }, untouched);
    }

    private static async Task RequireStoppedAsync(PartitionMovementPublicParentRf3NativeCut[][] cuts)
    {
        await PartitionMovementPublicParentRf3Cut.RequireCompactedAsync(cuts[FirstEntry]);
        await PartitionMovementPublicParentRf3Cut.RequireCompactedAsync(cuts[SecondEntry]);
        foreach (var untouched in cuts[ThirdEntry])
        {
            await Assert.That(untouched.Header).IsNull();
            await Assert.That(untouched.Pending).IsNull();
            await Assert.That(untouched.LastIssued).IsNull();
        }
    }

    private static async Task RequirePlacementsAsync(PartitionMovementPublicParentRf3Seed[] seeds,
        PartitionMoveRequest[] original, PartitionMoveResult[] terminals, CancellationToken token)
    {
        var revision = seeds[FirstEntry].OriginalPlacement.DirectoryRevision;
        var publications = new[] { checked(revision + SettledBeforeRepair), checked(revision + RevisionIncrement),
            checked(revision + original.Length) };
        for (var index = FirstEntry; index < seeds.Length; index++)
        {
            await PhysicalShardPartitionMovementRf3Placement.RequireAsync(seeds[index], original[index],
                seeds[index].OriginalPlacement, seeds[index].Directory.ControlOwner, terminals[index],
                publications[index], checked(revision + original.Length), token);
        }
    }
}
