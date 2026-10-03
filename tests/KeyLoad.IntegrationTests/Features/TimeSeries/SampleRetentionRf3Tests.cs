using System.Collections.Immutable;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using TUnit.Assertions.Enums;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

/// <summary>Logged retention remains visible and retry safe through actual RF3 SDK and MCP.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class SampleRetentionRf3Tests(ClusterFixture fixture)
{
    private const string ProblemCode = "code";
    [Test]
    public async Task RetentionPagesAndReplayPreserveOneLogicalFloorAcrossSdkAndOfficialMcp()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await PrepareAsync(deadline.Token);
        using var writerHttp = McpCallerHttp.Create(fixture, "node1");
        using var readerHttp = McpCallerHttp.Create(fixture, "node3");
        var writer = new KeyLoadClient(writerHttp, fixture.AdminKey);
        var reader = new KeyLoadClient(readerHttp, fixture.AdminKey);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, "node2", fixture.AdminKey, deadline.Token);
        var cutoff = TimeSeriesRf3Scenario.Start.AddMinutes(3);
        var command = new CommandRequest(Guid.NewGuid(), scenario.Partition,
            [new ExpireSamples(TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.Series, cutoff, 1)]);
        var committed = await McpCallerAssertions.SdkSuccessAsync(await writer.CommitAsync(command, deadline.Token));
        var replay = await McpCallerAssertions.SdkSuccessAsync(await reader.CommitAsync(command, deadline.Token));
        await Assert.That(replay.Token).IsEqualTo(committed.Token);
        await AssertStatusAsync(reader, mcp, scenario, new(cutoff, 1, true), deadline.Token);
        await AssertSurvivorsAsync(reader, mcp, scenario, deadline.Token);

        var next = command with
        {
            CommandId = Guid.NewGuid(),
            Mutations =
            [new ExpireSamples(TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.Series, cutoff, 2)]
        };
        await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await mcp.CallAsync(McpCallerTools.DocumentsCommit, next, deadline.Token));
        await AssertStatusAsync(reader, mcp, scenario, new(cutoff, 3, false), deadline.Token);
        await scenario.AppendAsync(fixture, TimeSeriesRf3Scenario.Series,
            [TimeSeriesRf3Scenario.Data("retention-0", TimeSeriesRf3Scenario.Start, 0)],
            TimeSeriesRf3Scenario.PublicTags, deadline.Token);
        await AssertSurvivorsAsync(reader, mcp, scenario, deadline.Token);
    }

    [Test]
    public async Task SeriesReadCannotAdvanceRetentionButCanReadItsPersistedProgress()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await PrepareAsync(deadline.Token);
        var identity = await McpPersistedIdentity.CreateAsync(fixture, scenario.Partition,
            TimeSeriesRf3Scenario.Set, Capability.SeriesRead, deadline.Token);
        using var http = McpCallerHttp.Create(fixture, "node3");
        var reader = new KeyLoadClient(http, identity.Secret);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, "node2", identity.Secret, deadline.Token);
        var command = new CommandRequest(Guid.NewGuid(), scenario.Partition,
            [new ExpireSamples(TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.Series,
                TimeSeriesRf3Scenario.Start.AddMinutes(3), 1)]);
        var denied = await reader.CommitAsync(command, deadline.Token);
        await Assert.That(denied.IsSuccess).IsFalse();
        await Assert.That(denied.Problem!.Extensions![ProblemCode]?.ToString()).IsEqualTo(ErrorCode.PermissionDenied.ToString());
        await McpCallerAssertions.ErrorAsync(
            await mcp.CallAsync(McpCallerTools.DocumentsCommit, command, deadline.Token), ErrorCode.PermissionDenied, true);
        await AssertStatusAsync(reader, mcp, scenario, new(null, 0, false), deadline.Token);
    }

    private async Task<TimeSeriesRf3Scenario> PrepareAsync(CancellationToken token)
    {
        var scenario = await TimeSeriesRf3Scenario.CreateAsync(fixture, token);
        var samples = Enumerable.Range(0, 5).Select(index => TimeSeriesRf3Scenario.Data(
            "retention-" + index, TimeSeriesRf3Scenario.Start.AddMinutes(index), index)).ToImmutableArray();
        await scenario.AppendAsync(fixture, TimeSeriesRf3Scenario.Series, samples, TimeSeriesRf3Scenario.PublicTags, token);
        return scenario;
    }

    private static async Task AssertStatusAsync(KeyLoadClient reader, McpOfficialClient mcp,
        TimeSeriesRf3Scenario scenario, SampleRetentionStatus expected, CancellationToken token)
    {
        var request = new ReadSampleRetentionRequest(scenario.Partition, TimeSeriesRf3Scenario.Set,
            TimeSeriesRf3Scenario.Series);
        var actual = await McpCallerAssertions.SdkSuccessAsync(await reader.ReadSampleRetentionAsync(request, token));
        var native = await McpCallerAssertions.SuccessAsync<SampleRetentionStatus>(
            await mcp.CallAsync(McpCallerTools.SeriesRetention, request, token));
        await Assert.That(actual).IsEqualTo(expected);
        await Assert.That(native.Value).IsEqualTo(expected);
    }

    private static async Task AssertSurvivorsAsync(KeyLoadClient reader, McpOfficialClient mcp,
        TimeSeriesRf3Scenario scenario, CancellationToken token)
    {
        var request = new ReadSamplesRequest(scenario.Partition, TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.Series,
            TimeSeriesRf3Scenario.Start, TimeSeriesRf3Scenario.Start.AddMinutes(5), 10);
        var samples = await McpCallerAssertions.SdkSuccessAsync(await reader.ReadSamplesAsync(request, token));
        var native = await McpCallerAssertions.SuccessAsync<SampleRecord[]>(
            await mcp.CallAsync(McpCallerTools.SeriesRead, request, token));
        await Assert.That(samples.Select(sample => sample.Sample.EventId)).IsEquivalentTo(
            new[] { "retention-3", "retention-4" }, CollectionOrdering.Matching);
        await Assert.That(native.Value.Select(sample => sample.Sample.EventId)).IsEquivalentTo(
            new[] { "retention-3", "retention-4" }, CollectionOrdering.Matching);
    }
}
