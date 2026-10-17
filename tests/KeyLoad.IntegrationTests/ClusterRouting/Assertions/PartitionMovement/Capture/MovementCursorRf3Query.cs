using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class MovementCursorRf3Query
{
    internal static async Task<MovementCursorRf3Pages> CaptureAsync(
        PartitionMovementPublicParentRf3Seed seed, CancellationToken token)
    {
        var request = MovementCursorRf3Corpus.Page(seed.Partition);
        var source = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.QueryAsync(request, token));
        var target = await McpCallerAssertions.SdkSuccessAsync(await seed.Target.QueryAsync(request, token));
        var official = (await McpCallerAssertions.SuccessAsync<QueryPage>(
            await seed.Official.CallAsync(McpCallerTools.QueryExecute, request, token))).Value;
        foreach (var page in new[] { source, target, official })
        {
            await MovementCursorRf3Assertions.FirstAsync(page);
        }
        var pages = new MovementCursorRf3Pages(source, target, official);
        await ContinueAsync(seed, pages, token);
        return pages;
    }

    internal static async Task ContinueAsync(PartitionMovementPublicParentRf3Seed seed,
        MovementCursorRf3Pages original, CancellationToken token)
    {
        await MovementCursorRf3Assertions.SecondAsync(
            await McpCallerAssertions.SdkSuccessAsync(await seed.Source.QueryAsync(
                MovementCursorRf3Corpus.Page(seed.Partition, original.Source.Cursor), token)), original.Source);
        await MovementCursorRf3Assertions.SecondAsync(
            await McpCallerAssertions.SdkSuccessAsync(await seed.Target.QueryAsync(
                MovementCursorRf3Corpus.Page(seed.Partition, original.Target.Cursor), token)), original.Target);
        await MovementCursorRf3Assertions.SecondAsync((await McpCallerAssertions.SuccessAsync<QueryPage>(
            await seed.Official.CallAsync(McpCallerTools.QueryExecute,
                MovementCursorRf3Corpus.Page(seed.Partition, original.Official.Cursor), token))).Value, original.Official);
    }

    internal static async Task RefuseAsync(PartitionMovementPublicParentRf3Seed seed,
        MovementCursorRf3Pages original, CancellationToken token)
    {
        var source = await seed.Source.QueryAsync(
            MovementCursorRf3Corpus.Page(seed.Partition, original.Source.Cursor), token);
        await Assert.That(source.IsSuccess).IsFalse();
        await Assert.That(source.Value).IsNull();
        await Assert.That(source.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.CursorExpired));
        var target = await seed.Target.QueryAsync(
            MovementCursorRf3Corpus.Page(seed.Partition, original.Target.Cursor), token);
        await Assert.That(target.IsSuccess).IsFalse();
        await Assert.That(target.Value).IsNull();
        await Assert.That(target.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.CursorExpired));
        await McpCallerAssertions.ErrorAsync(await seed.Official.CallAsync(McpCallerTools.QueryExecute,
            MovementCursorRf3Corpus.Page(seed.Partition, original.Official.Cursor), token),
            ErrorCode.CursorExpired, dispatched: true);
    }
}
