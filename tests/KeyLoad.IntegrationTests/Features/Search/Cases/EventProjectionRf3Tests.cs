using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Search;

/// <summary>AC-LINEAGE-001/002/003: explicit projection operations on the real RF3 cluster.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class EventProjectionRf3Tests(ClusterFixture fixture)
{
    private const double FirstVectorScore = 1d / 61d;
    private const double SecondVectorScore = 1d / 62d;

    [Test]
    public async Task AcLineage001ExactReplayConflictAndStaleInputPreserveOnlyTheCommittedProjection()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await EventProjectionRf3Scenario.CreateAsync(fixture, deadline.Token);
        using var clients = new EventProjectionRf3Clients(fixture, scenario,
            [McpCallerProtocol.Node1, McpCallerProtocol.Node2, McpCallerProtocol.Node3]);
        await using var workerMcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
            scenario.WorkerSecret, deadline.Token);
        await using var readerMcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
            scenario.ReaderSecret, deadline.Token);

        var first = await McpCallerAssertions.SdkSuccessAsync(await clients.Callers[1].CommitAsync(
            scenario.ApplyCommand(), deadline.Token));
        await EventProjectionRf3Assertions.AssertReceiptAsync(first, scenario);
        await VerifyOfficialReplayAsync(workerMcp, scenario, first, deadline.Token);
        await EventProjectionRf3Assertions.VerifyVectorBothAsync(clients.Readers[1], readerMcp,
            scenario.VectorSearch(), scenario.ReaderSecret, InitialResults(), deadline.Token);
        await VerifyChangedOutputAsync(clients, workerMcp, readerMcp, scenario, deadline.Token);
        await McpCallerAssertions.SdkSuccessAsync(await clients.Administrators[0].CommitAsync(
            scenario.ChangeSourceCommand(), deadline.Token));
        await VerifyStaleSourceAsync(clients.Callers[1], clients.Readers[1], workerMcp, readerMcp,
            scenario, first, deadline.Token);
        await EventProjectionRf3Scenario.ReclassifySourceAsync(fixture, scenario.Partition, deadline.Token);
        await VerifyReclassifiedSourceAsync(clients.Callers[1], clients.Readers[1], workerMcp, readerMcp,
            scenario, deadline.Token);
    }

    [Test]
    public async Task AcLineage002SourceHiddenFromPersistedReaderCannotProduceVisibleDerivedVector()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await EventProjectionRf3Scenario.CreateAsync(fixture, deadline.Token);
        using var workerHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var worker = new KeyLoadClient(workerHttp, scenario.WorkerSecret);
        await McpCallerAssertions.SdkSuccessAsync(await worker.CommitAsync(scenario.ApplyCommand(), deadline.Token));
        var restricted = await EventProjectionRf3Scenario.CreateReaderAsync(fixture, scenario.Partition,
            true, EventProjectionRf3Scenario.TargetOwner, deadline.Token);
        using var callerHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var caller = new KeyLoadClient(callerHttp, restricted.Secret);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
            restricted.Secret, deadline.Token);
        await EventProjectionRf3Assertions.VerifyVectorBothAsync(caller, mcp, scenario.VectorSearch(),
            restricted.Secret, BaselineOnly(), deadline.Token);
    }

    [Test]
    public async Task AcLineage003CommittedProjectionReceiptAndLiveVectorSurviveLeaderLossAndRestart()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await EventProjectionRf3Scenario.CreateAsync(fixture, deadline.Token);
        var nodes = new[] { McpCallerProtocol.Node1, McpCallerProtocol.Node2, McpCallerProtocol.Node3 };
        using var clients = new EventProjectionRf3Clients(fixture, scenario, nodes);
        var original = await McpCallerAssertions.SdkSuccessAsync(await clients.Callers[0].CommitAsync(
            scenario.ApplyCommand(), deadline.Token));
        await EventProjectionRf3Assertions.AssertReceiptAsync(original, scenario);
        await new EventProjectionRf3LeaderLoss(fixture).RunAsync(scenario, clients, original, nodes,
            deadline.Token);
    }

    private static async Task VerifyOfficialReplayAsync(McpOfficialClient workerMcp,
        EventProjectionRf3Scenario scenario, CommitReceipt first, CancellationToken cancellationToken)
    {
        var response = await workerMcp.CallAsync(McpCallerTools.DocumentsCommit,
            scenario.ApplyCommand(), cancellationToken);
        var replay = await McpCallerAssertions.SuccessAsync<CommitReceipt>(response);
        await McpCallerAssertions.DoesNotDiscloseAsync(response, scenario.WorkerSecret,
            EventProjectionRf3Scenario.Secret);
        await Assert.That(JsonDefaults.Serialize(replay.Value).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(first))).IsTrue();
    }

    private static async Task VerifyChangedOutputAsync(EventProjectionRf3Clients clients,
        McpOfficialClient workerMcp, McpOfficialClient readerMcp, EventProjectionRf3Scenario scenario,
        CancellationToken cancellationToken)
    {
        var changed = scenario.Projection with
        { Target = scenario.Projection.Target with { Values = [0, 1] } };
        var command = scenario.ApplyCommand(Guid.NewGuid(), changed);
        await EventProjectionRf3Assertions.AssertSdkFailureAsync(
            await clients.Callers[1].CommitAsync(command, cancellationToken), ErrorCode.Conflict);
        var response = await workerMcp.CallAsync(McpCallerTools.DocumentsCommit, command, cancellationToken);
        await McpCallerAssertions.ErrorAsync(response, ErrorCode.Conflict, dispatched: true);
        await McpCallerAssertions.DoesNotDiscloseAsync(response, scenario.WorkerSecret,
            EventProjectionRf3Scenario.Secret);
        await EventProjectionRf3Assertions.VerifyVectorBothAsync(clients.Readers[1], readerMcp,
            scenario.VectorSearch(), scenario.ReaderSecret, InitialResults(), cancellationToken);
    }

    private static async Task VerifyStaleSourceAsync(KeyLoadClient worker, KeyLoadClient reader,
        McpOfficialClient workerMcp, McpOfficialClient readerMcp, EventProjectionRf3Scenario scenario,
        CommitReceipt original, CancellationToken cancellationToken)
    {
        var replay = await McpCallerAssertions.SdkSuccessAsync(await worker.CommitAsync(
            scenario.ApplyCommand(), cancellationToken));
        await Assert.That(JsonDefaults.Serialize(replay).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(original))).IsTrue();
        var stale = scenario.ApplyCommand(Guid.NewGuid());
        await EventProjectionRf3Assertions.AssertSdkFailureAsync(await worker.CommitAsync(stale,
            cancellationToken), ErrorCode.RevisionConflict);
        await McpCallerAssertions.ErrorAsync(await workerMcp.CallAsync(McpCallerTools.DocumentsCommit,
            stale, cancellationToken), ErrorCode.RevisionConflict, dispatched: true);
        await EventProjectionRf3Assertions.VerifyVectorBothAsync(reader, readerMcp,
            scenario.VectorSearch(), scenario.ReaderSecret, BaselineOnly(), cancellationToken);
    }

    private static async Task VerifyReclassifiedSourceAsync(KeyLoadClient worker, KeyLoadClient reader,
        McpOfficialClient workerMcp, McpOfficialClient readerMcp, EventProjectionRf3Scenario scenario,
        CancellationToken cancellationToken)
    {
        await EventProjectionRf3Assertions.AssertSdkFailureAsync(await worker.CommitAsync(
            scenario.ApplyCommand(), cancellationToken), ErrorCode.PermissionDenied);
        await McpCallerAssertions.ErrorAsync(await workerMcp.CallAsync(McpCallerTools.DocumentsCommit,
            scenario.ApplyCommand(), cancellationToken), ErrorCode.PermissionDenied, dispatched: true);
        await EventProjectionRf3Assertions.VerifyVectorBothAsync(reader, readerMcp,
            scenario.VectorSearch(), scenario.ReaderSecret, BaselineOnly(), cancellationToken);
    }

    private static (string Id, double Score)[] InitialResults() =>
    [
        (EventProjectionRf3Scenario.TargetId, FirstVectorScore),
        (EventProjectionRf3Scenario.BaselineId, SecondVectorScore)
    ];

    private static (string Id, double Score)[] BaselineOnly() =>
        [(EventProjectionRf3Scenario.BaselineId, FirstVectorScore)];
}
