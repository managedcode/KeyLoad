using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using ManagedCode.Communication;
using TUnit.Assertions.Enums;

namespace KeyLoad.IntegrationTests.TimeSeries;

internal static class SampleRetentionClusterMutationAssertions
{
    internal static async Task VerifyPersistedGrantsAsync(ClusterFixture fixture, McpOfficialClient adminMcp,
        TimeSeriesClusterScenario scenario, DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        var reader = await CreateCallerAsync(fixture, scenario, Capability.SeriesRead, cancellationToken);
        var manager = await CreateCallerAsync(fixture, scenario, Capability.SeriesManage, cancellationToken);
        await using var readerMcp = await McpOfficialClient.ConnectAsync(fixture,
            TimeSeriesClusterScenario.Node3, reader.Identity.Secret, cancellationToken);
        await using var managerMcp = await McpOfficialClient.ConnectAsync(fixture,
            TimeSeriesClusterScenario.Node1, manager.Identity.Secret, cancellationToken);
        var request = RetentionRequest(scenario);
        var status = new SampleRetentionStatus(cutoff, 1, true);
        var readerStatus = await McpCallerAssertions.SdkSuccessAsync(await reader.Client.ReadSampleRetentionAsync(
            request, cancellationToken));
        var mcpStatus = await McpCallerAssertions.SuccessAsync<SampleRetentionStatus>(
            await readerMcp.CallAsync(McpCallerTools.SeriesRetention, request, cancellationToken));
        await Assert.That(readerStatus).IsEqualTo(status);
        await Assert.That(mcpStatus.Value).IsEqualTo(status);
        await VerifyManageDeniedAsync(reader.Client, readerMcp, scenario, cutoff, cancellationToken);
        await VerifyReadDeniedAsync(manager.Client, managerMcp, request, cancellationToken);
        var adminStatus = await McpCallerAssertions.SuccessAsync<SampleRetentionStatus>(
            await adminMcp.CallAsync(McpCallerTools.SeriesRetention, request, cancellationToken));
        await Assert.That(adminStatus.Value).IsEqualTo(status);
    }

    internal static async Task VerifyLateAppendBehaviorAsync(ClusterFixture fixture, McpOfficialClient mcp,
        TimeSeriesClusterScenario scenario, DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        var identical = AppendCommand(scenario, "retention-0", TimeSeriesClusterScenario.Start, 1);
        var sdkReceipt = await McpCallerAssertions.SdkSuccessAsync(await fixture.Client(TimeSeriesClusterScenario.Node1)
            .CommitAsync(identical, cancellationToken));
        var mcpReceipt = await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await mcp.CallAsync(McpCallerTools.DocumentsCommit, identical, cancellationToken));
        await Assert.That(mcpReceipt.Value.Token).IsEqualTo(sdkReceipt.Token);
        var retained = await McpCallerAssertions.SdkSuccessAsync(await fixture.Client(TimeSeriesClusterScenario.Node3)
            .ReadSamplesAsync(ReadRequest(scenario), cancellationToken));
        await Assert.That(retained.Select(record => record.Sample.EventId))
            .IsEquivalentTo(new[] { "retention-3" }, CollectionOrdering.Matching);

        var expired = AppendCommand(scenario, "new-expired-id", TimeSeriesClusterScenario.Start, 9);
        await AssertProblemAsync(await fixture.Client(TimeSeriesClusterScenario.Node1)
            .CommitAsync(expired, cancellationToken), ErrorCode.HistoryUnavailable);
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.DocumentsCommit,
            expired, cancellationToken), ErrorCode.HistoryUnavailable, dispatched: true);
        await AppendAtFloorAsync(fixture, mcp, scenario, cutoff, cancellationToken);
    }

    private static async Task VerifyManageDeniedAsync(KeyLoadClient reader, McpOfficialClient readerMcp,
        TimeSeriesClusterScenario scenario, DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        var command = ExpireCommand(scenario, cutoff);
        await AssertProblemAsync(await reader.CommitAsync(command, cancellationToken), ErrorCode.PermissionDenied);
        await McpCallerAssertions.ErrorAsync(await readerMcp.CallAsync(McpCallerTools.DocumentsCommit,
            command, cancellationToken), ErrorCode.PermissionDenied, dispatched: true);
    }

    private static async Task VerifyReadDeniedAsync(KeyLoadClient manager, McpOfficialClient managerMcp,
        ReadSampleRetentionRequest request, CancellationToken cancellationToken)
    {
        await AssertProblemAsync(await manager.ReadSampleRetentionAsync(request, cancellationToken), ErrorCode.PermissionDenied);
        await McpCallerAssertions.ErrorAsync(await managerMcp.CallAsync(McpCallerTools.SeriesRetention,
            request, cancellationToken), ErrorCode.PermissionDenied, dispatched: true);
    }

    private static async Task AppendAtFloorAsync(ClusterFixture fixture, McpOfficialClient mcp,
        TimeSeriesClusterScenario scenario, DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        var append = AppendCommand(scenario, "at-retention-floor", cutoff, 16);
        _ = await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await mcp.CallAsync(McpCallerTools.DocumentsCommit, append, cancellationToken));
        var range = await McpCallerAssertions.SdkSuccessAsync(await fixture.Client(TimeSeriesClusterScenario.Node1)
            .ReadSamplesAsync(ReadRequest(scenario), cancellationToken));
        await Assert.That(range.Select(row => row.Sample.EventId)).IsEquivalentTo(
            new[] { "retention-3", "at-retention-floor" }, CollectionOrdering.Matching);
        await Assert.That(range.Select(row => row.Sequence)).IsEquivalentTo(new long[] { 4, 5 }, CollectionOrdering.Matching);
    }

    private static async Task<(McpPersistedIdentity Identity, KeyLoadClient Client)> CreateCallerAsync(
        ClusterFixture fixture, TimeSeriesClusterScenario scenario, Capability capability,
        CancellationToken cancellationToken)
    {
        var identity = await McpPersistedIdentity.CreateAsync(fixture, scenario.Partition,
            TimeSeriesClusterScenario.Set, capability, cancellationToken);
        return (identity, fixture.Client(TimeSeriesClusterScenario.Node2, identity.Secret));
    }

    private static async Task AssertProblemAsync<T>(Result<T> result, ErrorCode expected)
    {
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Problem?.ErrorCode).IsEqualTo(expected.ToString());
    }

    private static CommandRequest ExpireCommand(TimeSeriesClusterScenario scenario, DateTimeOffset cutoff)
        => new(Guid.NewGuid(), scenario.Partition,
            [new ExpireSamples(TimeSeriesClusterScenario.Set, TimeSeriesClusterScenario.Series, cutoff, 1)]);

    private static CommandRequest AppendCommand(TimeSeriesClusterScenario scenario, string id,
        DateTimeOffset timestamp, double value)
        => new(Guid.NewGuid(), scenario.Partition,
            [new AppendSamples(TimeSeriesClusterScenario.Set, TimeSeriesClusterScenario.Series,
                [new(id, timestamp, value)], TimeSeriesClusterScenario.PublicTags)]);

    private static ReadSampleRetentionRequest RetentionRequest(TimeSeriesClusterScenario scenario)
        => new(scenario.Partition, TimeSeriesClusterScenario.Set, TimeSeriesClusterScenario.Series);

    private static ReadSamplesRequest ReadRequest(TimeSeriesClusterScenario scenario)
        => new(scenario.Partition, TimeSeriesClusterScenario.Set, TimeSeriesClusterScenario.Series,
            TimeSeriesClusterScenario.Start, TimeSeriesClusterScenario.Start.AddMinutes(4), 10);
}
