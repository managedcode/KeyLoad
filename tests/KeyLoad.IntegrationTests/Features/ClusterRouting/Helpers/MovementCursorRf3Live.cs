using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class MovementCursorRf3Live
{
    internal static async Task<MovementCursorRf3Lives> CaptureAsync(
        PartitionMovementPublicParentRf3Seed seed, CancellationToken token)
    {
        var request = new StartLiveQueryRequest(MovementCursorRf3Corpus.Live(seed.Partition));
        var source = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.StartLiveQueryAsync(request, token));
        var target = await McpCallerAssertions.SdkSuccessAsync(await seed.Target.StartLiveQueryAsync(request, token));
        var official = (await McpCallerAssertions.SuccessAsync<LiveQuerySnapshot>(
            await seed.Official.CallAsync(McpCallerTools.QueryLiveStart, request, token))).Value;
        foreach (var snapshot in new[] { source, target, official })
        {
            await MovementCursorRf3Assertions.LiveSnapshotAsync(snapshot);
        }
        var lives = new MovementCursorRf3Lives(source, target, official);
        await ContinueAsync(seed, lives, token);
        return lives;
    }

    internal static async Task ContinueAsync(PartitionMovementPublicParentRf3Seed seed,
        MovementCursorRf3Lives original, CancellationToken token)
    {
        var request = MovementCursorRf3Corpus.Live(seed.Partition);
        await MovementCursorRf3Assertions.LiveEmptyAsync(await McpCallerAssertions.SdkSuccessAsync(
            await seed.Source.ReadLiveQueryAsync(new(request, original.Source.Cursor), token)), original.Source);
        await MovementCursorRf3Assertions.LiveEmptyAsync(await McpCallerAssertions.SdkSuccessAsync(
            await seed.Target.ReadLiveQueryAsync(new(request, original.Target.Cursor), token)), original.Target);
        await MovementCursorRf3Assertions.LiveEmptyAsync((await McpCallerAssertions.SuccessAsync<LiveQueryPage>(
            await seed.Official.CallAsync(McpCallerTools.QueryLiveRead,
                new ReadLiveQueryRequest(request, original.Official.Cursor), token))).Value, original.Official);
    }

    internal static async Task RefuseAsync(PartitionMovementPublicParentRf3Seed seed,
        MovementCursorRf3Lives original, CancellationToken token)
    {
        var request = MovementCursorRf3Corpus.Live(seed.Partition);
        var source = await seed.Source.ReadLiveQueryAsync(new(request, original.Source.Cursor), token);
        await Assert.That(source.IsSuccess).IsFalse();
        await Assert.That(source.Value).IsNull();
        await Assert.That(source.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.TokenInvalidated));
        var target = await seed.Target.ReadLiveQueryAsync(new(request, original.Target.Cursor), token);
        await Assert.That(target.IsSuccess).IsFalse();
        await Assert.That(target.Value).IsNull();
        await Assert.That(target.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.TokenInvalidated));
        await McpCallerAssertions.ErrorAsync(await seed.Official.CallAsync(McpCallerTools.QueryLiveRead,
            new ReadLiveQueryRequest(request, original.Official.Cursor), token),
            ErrorCode.TokenInvalidated, dispatched: true);
    }
}
