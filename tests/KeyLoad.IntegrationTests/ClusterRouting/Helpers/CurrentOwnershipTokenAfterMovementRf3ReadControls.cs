using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.IntegrationTests.Features.RelationalStorage;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class CurrentOwnershipTokenAfterMovementRf3ReadControls
{
    private const int PlacementVersion = 1;

    internal static async Task<(string Cursor, long PolicyEpoch)> CaptureAsync(
        PartitionMovementPublicParentRf3Seed seed, CommitToken minimum, CancellationToken token)
    {
        var applied = await WaitAsync(seed, minimum, token).ConfigureAwait(false);
        var cursor = await CaptureCursorAsync(seed, token).ConfigureAwait(false);
        return (cursor, applied.PolicyEpoch);
    }

    internal static async Task RefuseRepeatedPartitionAsync(PartitionMovementPublicParentRf3Seed seed,
        CancellationToken token)
    {
        var repeated = seed.FirstRequest with
        {
            MoveId = Guid.NewGuid()
        };
        Assert.ThrowsExactly<ArgumentException>(() =>
            seed.Source.MoveWholePartitionsAsync([seed.FirstRequest, repeated], token));
        var current = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.ReadAtomicPartitionPlacementAsync(
            new(PlacementVersion, seed.Partition), token).ConfigureAwait(false));
        await SqlRf3Protocol.EqualAsync(seed.OriginalPlacement, current);
        await seed.VerifyAsync(token).ConfigureAwait(false);
    }

    internal static async Task<string> CaptureCursorAsync(PartitionMovementPublicParentRf3Seed seed,
        CancellationToken token)
    {
        var request = new ReadChangeFeedRequest(seed.Partition, RelationalSqlRf3Tokens.Documents,
            Start: ChangeFeedStart.Now);
        var original = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.ReadChangesAsync(request, token));
        await CurrentOwnershipTokenAfterMovementRf3ReadAssertions.EmptyAsync(original);
        await CurrentOwnershipTokenAfterMovementRf3ReadAssertions.EmptyAsync(
            await McpCallerAssertions.SdkSuccessAsync(await seed.Target.ReadChangesAsync(request, token)));
        await CurrentOwnershipTokenAfterMovementRf3ReadAssertions.EmptyAsync(
            (await McpCallerAssertions.SuccessAsync<ChangeFeedPage>(
                await seed.Official.CallAsync(McpCallerTools.ChangesRead, request, token))).Value);
        return original.Cursor;
    }

    internal static async Task InvalidAsync(PartitionMovementPublicParentRf3Seed seed, CommitToken original,
        string cursor, CancellationToken token)
    {
        var wait = new WaitForIndexRequest(seed.Partition, RelationalSqlRf3Tokens.Table,
            RelationalSqlRf3Tokens.TitlePath, original);
        var feed = new ReadChangeFeedRequest(seed.Partition, RelationalSqlRf3Tokens.Documents, cursor);
        foreach (var sdk in new[] { seed.Source, seed.Target })
        {
            var waiting = await sdk.WaitForIndexAsync(wait, token).ConfigureAwait(false);
            await Assert.That(waiting.IsSuccess).IsFalse();
            await Assert.That(waiting.Value).IsNull();
            await Assert.That(waiting.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.TokenInvalidated));
            await PartitionMovementPublicRefusalRf3Oracle.SdkAsync(waiting.Problem, ErrorCode.TokenInvalidated,
                PartitionMovementPublicRefusalRf3Oracle.IndexMinimumDetail);
            var reading = await sdk.ReadChangesAsync(feed, token).ConfigureAwait(false);
            await Assert.That(reading.IsSuccess).IsFalse();
            await Assert.That(reading.Value).IsNull();
            await Assert.That(reading.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.TokenInvalidated));
            await PartitionMovementPublicRefusalRf3Oracle.SdkAsync(reading.Problem, ErrorCode.TokenInvalidated,
                PartitionMovementPublicRefusalRf3Oracle.FeedCursorDetail);
        }
        await PartitionMovementPublicRefusalRf3Oracle.McpAsync(await seed.Official.CallAsync(
            WaitForIndexProtocol.Tool, wait, token), ErrorCode.TokenInvalidated, PartitionMovementPublicRefusalRf3Oracle.SafeGenericDetail);
        await PartitionMovementPublicRefusalRf3Oracle.McpAsync(await seed.Official.CallAsync(
            McpCallerTools.ChangesRead, feed, token), ErrorCode.TokenInvalidated, PartitionMovementPublicRefusalRf3Oracle.SafeGenericDetail);
        await CurrentOwnershipTokenSqlRf3Assertions.RefusedAsync(seed, WaitForIndexProtocol.Tool, wait,
            PartitionMovementPublicRefusalRf3Oracle.IndexMinimumDetail,
            PartitionMovementPublicRefusalRf3Oracle.SafeGenericDetail, token);
    }

    internal static async Task HealthyAsync(PartitionMovementPublicParentRf3Seed seed, CommitToken current,
        long originalPolicyEpoch, string cursor, DocumentResult expected, CancellationToken token)
    {
        var waited = await WaitAsync(seed, current, token).ConfigureAwait(false);
        await Assert.That(waited.PolicyEpoch).IsEqualTo(originalPolicyEpoch);
        var request = new ReadChangeFeedRequest(seed.Partition, RelationalSqlRf3Tokens.Documents, cursor);
        foreach (var sdk in new[] { seed.Source, seed.Target })
        {
            await CurrentOwnershipTokenAfterMovementRf3ReadAssertions.ChangeAsync(
                await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadChangesAsync(request, token)),
                current, expected);
        }
        await CurrentOwnershipTokenAfterMovementRf3ReadAssertions.ChangeAsync(
            (await McpCallerAssertions.SuccessAsync<ChangeFeedPage>(
                await seed.Official.CallAsync(McpCallerTools.ChangesRead, request, token))).Value,
            current, expected);
    }

    private static async Task<WaitForIndexResult> WaitAsync(PartitionMovementPublicParentRf3Seed seed,
        CommitToken minimum, CancellationToken token)
    {
        var request = new WaitForIndexRequest(seed.Partition, RelationalSqlRf3Tokens.Table,
            RelationalSqlRf3Tokens.TitlePath, minimum);
        var original = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.WaitForIndexAsync(request, token));
        await CurrentOwnershipTokenAfterMovementRf3ReadAssertions.WaitAsync(
            original, minimum, seed.Models.TableDefinition.SchemaVersion);
        var target = await McpCallerAssertions.SdkSuccessAsync(await seed.Target.WaitForIndexAsync(request, token));
        await CurrentOwnershipTokenAfterMovementRf3ReadAssertions.WaitAsync(
            target, minimum, seed.Models.TableDefinition.SchemaVersion);
        await Assert.That(target.PolicyEpoch).IsEqualTo(original.PolicyEpoch);
        var official = (await McpCallerAssertions.SuccessAsync<WaitForIndexResult>(
            await seed.Official.CallAsync(WaitForIndexProtocol.Tool, request, token))).Value;
        await CurrentOwnershipTokenAfterMovementRf3ReadAssertions.WaitAsync(
            official, minimum, seed.Models.TableDefinition.SchemaVersion);
        await Assert.That(official.PolicyEpoch).IsEqualTo(original.PolicyEpoch);
        await CurrentOwnershipTokenSqlRf3Assertions.WaitAsync(seed, request, original, token);
        return original;
    }
}
