using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using ManagedCode.Communication;
using TUnit.Assertions.Enums;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SampleRetentionRf3MutationAssertions
{
    internal static async Task VerifyPersistedGrantsAsync(ClusterFixture fixture, McpOfficialClient adminMcp,
        TimeSeriesRf3Scenario scenario, DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        var reader = await CreateCallerAsync(fixture, scenario, Capability.SeriesRead, cancellationToken);
        var manager = await CreateCallerAsync(fixture, scenario, Capability.SeriesManage, cancellationToken);
        await using var readerMcp = await McpOfficialClient.ConnectAsync(fixture,
            TimeSeriesRf3Scenario.Node3, reader.Identity.Secret, cancellationToken);
        await using var managerMcp = await McpOfficialClient.ConnectAsync(fixture,
            TimeSeriesRf3Scenario.Node1, manager.Identity.Secret, cancellationToken);
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
        TimeSeriesRf3Scenario scenario, DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        var identical = AppendCommand(scenario, "retention-0", TimeSeriesRf3Scenario.Start, 1);
        var sdkReceipt = await McpCallerAssertions.SdkSuccessAsync(await fixture.Client(TimeSeriesRf3Scenario.Node1)
            .CommitAsync(identical, cancellationToken));
        var mcpReceipt = await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await mcp.CallAsync(McpCallerTools.DocumentsCommit, identical, cancellationToken));
        await Assert.That(mcpReceipt.Value.Token).IsEqualTo(sdkReceipt.Token);
        var retained = await McpCallerAssertions.SdkSuccessAsync(await fixture.Client(TimeSeriesRf3Scenario.Node3)
            .ReadSamplesAsync(ReadRequest(scenario), cancellationToken));
        await Assert.That(retained.Select(record => record.Sample.EventId))
            .IsEquivalentTo(new[] { "retention-3" }, CollectionOrdering.Matching);

        var expired = AppendCommand(scenario, "new-expired-id", TimeSeriesRf3Scenario.Start, 9);
        await AssertProblemAsync(await fixture.Client(TimeSeriesRf3Scenario.Node1)
            .CommitAsync(expired, cancellationToken), ErrorCode.HistoryUnavailable);
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.DocumentsCommit,
            expired, cancellationToken), ErrorCode.HistoryUnavailable, dispatched: true);
        await AppendAtFloorAsync(fixture, mcp, scenario, cutoff, cancellationToken);
    }

    private static async Task VerifyManageDeniedAsync(KeyLoadClient reader, McpOfficialClient readerMcp,
        TimeSeriesRf3Scenario scenario, DateTimeOffset cutoff, CancellationToken cancellationToken)
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
        TimeSeriesRf3Scenario scenario, DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        var append = AppendCommand(scenario, "at-retention-floor", cutoff, 16);
        _ = await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await mcp.CallAsync(McpCallerTools.DocumentsCommit, append, cancellationToken));
        var range = await McpCallerAssertions.SdkSuccessAsync(await fixture.Client(TimeSeriesRf3Scenario.Node1)
            .ReadSamplesAsync(ReadRequest(scenario), cancellationToken));
        await Assert.That(range.Select(row => row.Sample.EventId)).IsEquivalentTo(
            new[] { "retention-3", "at-retention-floor" }, CollectionOrdering.Matching);
        await Assert.That(range.Select(row => row.Sequence)).IsEquivalentTo(new long[] { 4, 5 }, CollectionOrdering.Matching);
    }

    private static async Task<(McpPersistedIdentity Identity, KeyLoadClient Client)> CreateCallerAsync(
        ClusterFixture fixture, TimeSeriesRf3Scenario scenario, Capability capability,
        CancellationToken cancellationToken)
    {
        var identity = await McpPersistedIdentity.CreateAsync(fixture, scenario.Partition,
            TimeSeriesRf3Scenario.Set, capability, cancellationToken);
        return (identity, fixture.Client(TimeSeriesRf3Scenario.Node2, identity.Secret));
    }

    private static async Task AssertProblemAsync<T>(Result<T> result, ErrorCode expected)
    {
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Problem?.ErrorCode).IsEqualTo(expected.ToString());
    }

    private static CommandRequest ExpireCommand(TimeSeriesRf3Scenario scenario, DateTimeOffset cutoff)
        => new(Guid.NewGuid(), scenario.Partition,
            [new ExpireSamples(TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.Series, cutoff, 1)]);

    private static CommandRequest AppendCommand(TimeSeriesRf3Scenario scenario, string id,
        DateTimeOffset timestamp, double value)
        => new(Guid.NewGuid(), scenario.Partition,
            [new AppendSamples(TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.Series,
                [new(id, timestamp, value)], TimeSeriesRf3Scenario.PublicTags)]);

    private static ReadSampleRetentionRequest RetentionRequest(TimeSeriesRf3Scenario scenario)
        => new(scenario.Partition, TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.Series);

    private static ReadSamplesRequest ReadRequest(TimeSeriesRf3Scenario scenario)
        => new(scenario.Partition, TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.Series,
            TimeSeriesRf3Scenario.Start, TimeSeriesRf3Scenario.Start.AddMinutes(4), 10);
}
