using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.Search;

/// <summary>AC-LINEAGE-001/002/003: explicit projection operations on the real Orleans cluster.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class EventProjectionClusterTests(ClusterFixture fixture)
{
    private const double FirstVectorScore = 1d / 61d;
    private const double SecondVectorScore = 1d / 62d;

    [Test]
    public async Task AcLineage001ExactReplayConflictAndStaleInputPreserveOnlyTheCommittedProjection()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await EventProjectionClusterScenario.CreateAsync(fixture, deadline.Token);
        using var clients = new EventProjectionClusterClients(fixture, scenario,
            [McpCallerProtocol.Node1, McpCallerProtocol.Node2, McpCallerProtocol.Node3]);
        await using var workerMcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
            scenario.WorkerSecret, deadline.Token);
        await using var readerMcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
            scenario.ReaderSecret, deadline.Token);

        var first = await McpCallerAssertions.SdkSuccessAsync(await clients.Callers[1].CommitAsync(
            scenario.ApplyCommand(), deadline.Token));
        await EventProjectionClusterAssertions.AssertReceiptAsync(first, scenario);
        await VerifyOfficialReplayAsync(workerMcp, scenario, first, deadline.Token);
        await EventProjectionClusterAssertions.VerifyVectorBothAsync(clients.Readers[1], readerMcp,
            scenario, scenario.ReaderSecret, InitialResults(), deadline.Token);
        await VerifyChangedOutputAsync(clients, workerMcp, readerMcp, scenario, deadline.Token);
        await McpCallerAssertions.SdkSuccessAsync(await clients.Administrators[0].CommitAsync(
            scenario.ChangeSourceCommand(), deadline.Token));
        await VerifyStaleSourceAsync(clients.Callers[1], clients.Readers[1], workerMcp, readerMcp,
            scenario, first, deadline.Token);
        await EventProjectionClusterScenario.ReclassifySourceAsync(fixture, scenario.Partition, deadline.Token);
        await VerifyReclassifiedSourceAsync(clients.Callers[1], clients.Readers[1], workerMcp, readerMcp,
            scenario, deadline.Token);
    }

    [Test]
    public async Task AcLineage002SourceHiddenFromPersistedReaderCannotProduceVisibleDerivedVector()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await EventProjectionClusterScenario.CreateAsync(fixture, deadline.Token);
        var warm = await HiddenReaderWarmAsync(scenario, deadline.Token);
        await ProjectionPublicPolicyFlow.RunAsync(fixture, scenario, warm.Reader, warm.Original, deadline.Token);
    }

    private async Task<(CommitReceipt Original, McpPersistedIdentity Reader)> HiddenReaderWarmAsync(
        EventProjectionClusterScenario scenario, CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        (CommitReceipt Original, McpPersistedIdentity Reader) result = default;
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            using var workerHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                var worker = new KeyLoadClient(workerHttp, scenario.WorkerSecret, IntegrationClientOptions.Execution());
                var original = await McpCallerAssertions.SdkSuccessAsync(await worker.CommitAsync(
                    scenario.ApplyCommand(), cancellationToken));
                var restricted = await EventProjectionClusterScenario.CreateReaderAsync(fixture, scenario.Partition,
                    true, EventProjectionClusterScenario.TargetOwner, cancellationToken);
                using var callerHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
                await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                {
                    var caller = new KeyLoadClient(callerHttp, restricted.Secret, IntegrationClientOptions.Execution());
                    await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
                        restricted.Secret, cancellationToken);
                    await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                    {
                        await EventProjectionClusterAssertions.VerifyVectorBothAsync(caller, mcp, scenario,
                            restricted.Secret, BaselineOnly(), cancellationToken);
                        result = (original, restricted);
                    }, failures);
                }, failures);
            }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
        return result;
    }

    [Test]
    public async Task AcLineage003CommittedProjectionReceiptAndLiveVectorSurviveSiloLossAndRejoin()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await EventProjectionClusterScenario.CreateAsync(fixture, deadline.Token);
        var nodes = new[] { McpCallerProtocol.Node1, McpCallerProtocol.Node2, McpCallerProtocol.Node3 };
        using var clients = new EventProjectionClusterClients(fixture, scenario, nodes);
        var original = await McpCallerAssertions.SdkSuccessAsync(await clients.Callers[0].CommitAsync(
            scenario.ApplyCommand(), deadline.Token));
        await EventProjectionClusterAssertions.AssertReceiptAsync(original, scenario);
        await new EventProjectionClusterSiloRestart(fixture).RunAsync(scenario, clients, original, nodes,
            deadline.Token);
    }

    private static async Task VerifyOfficialReplayAsync(McpOfficialClient workerMcp,
        EventProjectionClusterScenario scenario, CommitReceipt first, CancellationToken cancellationToken)
    {
        var response = await workerMcp.CallAsync(McpCallerTools.DocumentsCommit,
            scenario.ApplyCommand(), cancellationToken);
        var replay = await McpCallerAssertions.SuccessAsync<CommitReceipt>(response);
        await McpCallerAssertions.DoesNotDiscloseAsync(response, scenario.WorkerSecret,
            EventProjectionClusterScenario.Secret);
        await Assert.That(JsonDefaults.Serialize(replay.Value).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(first))).IsTrue();
    }

    private static async Task VerifyChangedOutputAsync(EventProjectionClusterClients clients,
        McpOfficialClient workerMcp, McpOfficialClient readerMcp, EventProjectionClusterScenario scenario,
        CancellationToken cancellationToken)
    {
        var changed = scenario.Projection with
        {
            Target = scenario.Projection.Target with
            {
                Values = [0, 1]
            }
        };
        var command = scenario.ApplyCommand(Guid.NewGuid(), changed);
        await EventProjectionClusterAssertions.AssertSdkFailureAsync(
            await clients.Callers[1].CommitAsync(command, cancellationToken), ErrorCode.Conflict);
        var response = await workerMcp.CallAsync(McpCallerTools.DocumentsCommit, command, cancellationToken);
        _ = (await McpCallerAssertions.ErrorAsync(response, ErrorCode.Conflict, dispatched: true)).Value;
        await McpCallerAssertions.DoesNotDiscloseAsync(response, scenario.WorkerSecret,
            EventProjectionClusterScenario.Secret);
        await EventProjectionClusterAssertions.VerifyVectorBothAsync(clients.Readers[1], readerMcp,
            scenario, scenario.ReaderSecret, InitialResults(), cancellationToken);
    }

    private static async Task VerifyStaleSourceAsync(KeyLoadClient worker, KeyLoadClient reader,
        McpOfficialClient workerMcp, McpOfficialClient readerMcp, EventProjectionClusterScenario scenario,
        CommitReceipt original, CancellationToken cancellationToken)
    {
        var replay = await McpCallerAssertions.SdkSuccessAsync(await worker.CommitAsync(
            scenario.ApplyCommand(), cancellationToken));
        await Assert.That(JsonDefaults.Serialize(replay).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(original))).IsTrue();
        var stale = scenario.ApplyCommand(Guid.NewGuid());
        await EventProjectionClusterAssertions.AssertSdkFailureAsync(await worker.CommitAsync(stale,
            cancellationToken), ErrorCode.RevisionConflict);
        _ = (await McpCallerAssertions.ErrorAsync(await workerMcp.CallAsync(McpCallerTools.DocumentsCommit,
            stale, cancellationToken), ErrorCode.RevisionConflict, dispatched: true)).Value;
        await EventProjectionClusterAssertions.VerifyVectorBothAsync(reader, readerMcp,
            scenario, scenario.ReaderSecret, BaselineOnly(), cancellationToken);
    }

    private static async Task VerifyReclassifiedSourceAsync(KeyLoadClient worker, KeyLoadClient reader,
        McpOfficialClient workerMcp, McpOfficialClient readerMcp, EventProjectionClusterScenario scenario,
        CancellationToken cancellationToken)
    {
        await EventProjectionClusterAssertions.AssertSdkFailureAsync(await worker.CommitAsync(
            scenario.ApplyCommand(), cancellationToken), ErrorCode.PermissionDenied);
        _ = (await McpCallerAssertions.ErrorAsync(await workerMcp.CallAsync(McpCallerTools.DocumentsCommit,
            scenario.ApplyCommand(), cancellationToken), ErrorCode.PermissionDenied, dispatched: true)).Value;
        await EventProjectionClusterAssertions.VerifyVectorBothAsync(reader, readerMcp,
            scenario, scenario.ReaderSecret, BaselineOnly(), cancellationToken);
    }

    private static (string Id, double Score)[] InitialResults() =>
    [
        (EventProjectionClusterScenario.TargetId, FirstVectorScore),
        (EventProjectionClusterScenario.BaselineId, SecondVectorScore)
    ];

    private static (string Id, double Score)[] BaselineOnly() =>
        [(EventProjectionClusterScenario.BaselineId, FirstVectorScore)];
}
