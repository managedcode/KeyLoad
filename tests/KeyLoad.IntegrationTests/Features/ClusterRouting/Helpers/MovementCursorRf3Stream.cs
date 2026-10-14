using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.RelationalStorage;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class MovementCursorRf3Stream
{
    internal const string StreamId = "movement-cursor-events";
    internal static EventData First => new("a", "movement-cursor", "{\"order\":1}");
    internal static EventData Second => new("b", "movement-cursor", "{\"order\":2}");
    private const int SingleEvent = 1;

    internal static ReadStreamRequest Request(PartitionRef partition, string? cursor = null)
        => new(new(partition, RelationalSqlRf3Tokens.Streams, StreamId), Limit: SingleEvent, Cursor: cursor);

    internal static async Task<MovementCursorRf3Streams> CaptureAsync(
        PartitionMovementPublicParentRf3Seed seed, CancellationToken token)
    {
        var request = Request(seed.Partition);
        var source = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.ReadStreamAsync(request, token));
        var target = await McpCallerAssertions.SdkSuccessAsync(await seed.Target.ReadStreamAsync(request, token));
        var official = (await McpCallerAssertions.SuccessAsync<StreamPage>(
            await seed.Official.CallAsync(McpCallerTools.StreamsRead, request, token))).Value;
        foreach (var page in new[] { source, target, official })
        {
            await MovementCursorRf3StreamAssertions.FirstAsync(page, request.Stream);
        }
        var streams = new MovementCursorRf3Streams(source, target, official);
        await ContinueAsync(seed, streams, token);
        return streams;
    }

    internal static async Task ContinueAsync(PartitionMovementPublicParentRf3Seed seed,
        MovementCursorRf3Streams original, CancellationToken token)
    {
        await MovementCursorRf3StreamAssertions.SecondAsync(await McpCallerAssertions.SdkSuccessAsync(
            await seed.Source.ReadStreamAsync(Request(seed.Partition, original.Source.Cursor), token)), original.Source);
        await MovementCursorRf3StreamAssertions.SecondAsync(await McpCallerAssertions.SdkSuccessAsync(
            await seed.Target.ReadStreamAsync(Request(seed.Partition, original.Target.Cursor), token)), original.Target);
        await MovementCursorRf3StreamAssertions.SecondAsync((await McpCallerAssertions.SuccessAsync<StreamPage>(
            await seed.Official.CallAsync(McpCallerTools.StreamsRead,
                Request(seed.Partition, original.Official.Cursor), token))).Value, original.Official);
    }

    internal static async Task RefuseAsync(PartitionMovementPublicParentRf3Seed seed,
        MovementCursorRf3Streams original, CancellationToken token)
    {
        var source = await seed.Source.ReadStreamAsync(Request(seed.Partition, original.Source.Cursor), token);
        await Assert.That(source.IsSuccess).IsFalse();
        await Assert.That(source.Value).IsNull();
        await Assert.That(source.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.CursorExpired));
        var target = await seed.Target.ReadStreamAsync(Request(seed.Partition, original.Target.Cursor), token);
        await Assert.That(target.IsSuccess).IsFalse();
        await Assert.That(target.Value).IsNull();
        await Assert.That(target.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.CursorExpired));
        await McpCallerAssertions.ErrorAsync(await seed.Official.CallAsync(McpCallerTools.StreamsRead,
            Request(seed.Partition, original.Official.Cursor), token), ErrorCode.CursorExpired, dispatched: true);
    }
}
