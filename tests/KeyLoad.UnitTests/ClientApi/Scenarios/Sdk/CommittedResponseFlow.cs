using KeyLoad.Client;
using KeyLoad.Server;
using KeyLoad.UnitTests.Messaging;
using KeyLoad.UnitTests.OrleansExecution;

namespace KeyLoad.UnitTests.ClientApi;

internal static class CommittedResponseFlow
{
    internal static async Task RunAsync(ConnectionScenario scenario, bool callerCancellation)
    {
        await scenario.InitializeAsync(TestContext.Current!.Execution.CancellationToken);
        using var deadline = new CancellationTokenSource(scenario.Timing.CompletionTimeout, TimeProvider.System);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, TestContext.Current!.Execution.CancellationToken);
        var token = linked.Token;
        var principal = scenario.Principal();
        var credential = scenario.Fixture.Database.Submit(OperationKind.ConfigureApiKey, new ConfigureApiKeyRequest(
            global::KeyLoad.Core.AuthorizationExecution.Credential(CommittedResponseProtocol.ApiKeyId, principal.Id,
                CommittedResponseProtocol.ApiKey))).Get<bool>();
        await Assert.That(credential).IsTrue();
        var command = scenario.Command(CommittedResponseProtocol.Document, CommittedResponseProtocol.OriginalJson,
            CommittedResponseProtocol.AbsentRevision);
        var expected = CommittedResponseAssertions.Expected(scenario, command,
            checked(scenario.Fixture.Database.ReadCanonicalCut().AppliedPosition + CommittedResponseProtocol.CommitAdvance), CommittedResponseProtocol.FirstRevision);
        var endpoint = new CommittedResponseEndpoint(scenario, principal, callerCancellation);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var server = await KeyLoadClientKestrelServer.StartAsync(endpoint.HandleAsync);
            await ServerFailureObserver.ObserveAsync(() => ExecuteAsync(scenario, server, endpoint, command, expected, callerCancellation, token), failures);
        }, failures);
        if (endpoint.Failure.Task.IsCompletedSuccessfully)
        {
            failures.Add(await endpoint.Failure.Task);
        }
        await ServerFailureObserver.ObserveAsync(() => scenario.CloseAsync(token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExecuteAsync(ConnectionScenario scenario, KeyLoadClientKestrelServer server,
        CommittedResponseEndpoint endpoint, CommandRequest command, CommitReceipt expected,
        bool callerCancellation, CancellationToken token)
    {
        var client = new KeyLoadClient(server.Client, CommittedResponseProtocol.ApiKey, UnitClientOptions.Execution());
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(token);
        var original = client.CommitAsync(command, caller.Token);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var observed = await Task.WhenAny(endpoint.FirstChunk.Task, endpoint.Failure.Task, original).WaitAsync(token);
            if (observed != endpoint.FirstChunk.Task)
            {
                throw new InvalidOperationException(CommittedResponseProtocol.UnexpectedRequest, endpoint.Failure.Task.IsCompletedSuccessfully ? await endpoint.Failure.Task : null);
            }
            await Assert.That(original.IsCompleted).IsFalse();
            await CommittedResponseAssertions.ReceiptAsync(endpoint.Original, expected);
            await CommittedResponseAssertions.StoredAsync(scenario, CommittedResponseProtocol.FirstRevision, CommittedResponseProtocol.OriginalJson);
            if (callerCancellation)
            {
                await caller.CancelAsync();
            }
            else
            {
                endpoint.Release.TrySetResult();
            }
            var interrupted = await original.WaitAsync(token);
            await Assert.That(interrupted.IsFailed).IsTrue();
            await Assert.That(interrupted.Value).IsNull();
            await Assert.That(interrupted.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.UnknownWriteOutcome));
            await Assert.That(interrupted.Problem?.Detail).IsEqualTo(CommittedResponseProtocol.UnknownDetail);
            await Assert.That(JsonDefaults.Serialize(interrupted.Problem).SequenceEqual(JsonDefaults.Serialize(
                Errors.Problem(ErrorCode.UnknownWriteOutcome, CommittedResponseProtocol.UnknownDetail)))).IsTrue();
            await endpoint.Aborted.Task.WaitAsync(token);
            await endpoint.FirstJoined.Task.WaitAsync(token);
            await ContinueAsync(scenario, client, endpoint, command, expected, token);
        }, failures);
        endpoint.Release.TrySetResult();
        await ServerFailureObserver.ObserveAsync(caller.CancelAsync, failures);
        await ServerFailureObserver.ObserveAsync(async () => { _ = await original.WaitAsync(token); }, failures);
        await ServerFailureObserver.ObserveAsync(() => endpoint.FirstJoined.Task.WaitAsync(token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ContinueAsync(ConnectionScenario scenario, KeyLoadClient client,
        CommittedResponseEndpoint endpoint, CommandRequest command, CommitReceipt expected, CancellationToken token)
    {
        var bytes = QueueWholeFlowStorage.Bytes(scenario.Fixture.Database.Store);
        var position = scenario.Fixture.Database.Store.Position;
        var replay = await client.CommitAsync(command, token);
        await Assert.That(replay.IsSuccess).IsTrue();
        await CommittedResponseAssertions.ReceiptAsync(replay.Value, expected);
        await CommittedResponseAssertions.UnchangedAsync(scenario, bytes, position);
        var changed = command with
        {
            Mutations = [new PutDocument(ConnectionProtocol.Collection,
            CommittedResponseProtocol.Document, CommittedResponseProtocol.ChangedJson, CommittedResponseProtocol.FirstRevision)]
        };
        var refused = await client.CommitAsync(changed, token);
        await Assert.That(refused.IsFailed).IsTrue();
        await Assert.That(refused.Value).IsNull();
        await Assert.That(refused.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.Conflict));
        await Assert.That(JsonDefaults.Serialize(refused.Problem).SequenceEqual(JsonDefaults.Serialize(
            Errors.Problem(ErrorCode.Conflict, CommittedResponseProtocol.ConflictDetail)))).IsTrue();
        await CommittedResponseAssertions.UnchangedAsync(scenario, bytes, position);
        await CommittedResponseAssertions.StoredAsync(scenario, CommittedResponseProtocol.FirstRevision, CommittedResponseProtocol.OriginalJson);
        var healthy = scenario.Command(CommittedResponseProtocol.Document, CommittedResponseProtocol.HealthyJson, CommittedResponseProtocol.FirstRevision);
        var expectedHealthy = CommittedResponseAssertions.Expected(scenario, healthy,
            checked(scenario.Fixture.Database.ReadCanonicalCut().AppliedPosition + CommittedResponseProtocol.CommitAdvance), CommittedResponseProtocol.SecondRevision);
        var result = await client.CommitAsync(healthy, token);
        await Assert.That(result.IsSuccess).IsTrue();
        await CommittedResponseAssertions.ReceiptAsync(result.Value, expectedHealthy);
        await CommittedResponseAssertions.StoredAsync(scenario, CommittedResponseProtocol.SecondRevision, CommittedResponseProtocol.HealthyJson);
        var current = QueueWholeFlowStorage.Bytes(scenario.Fixture.Database.Store);
        var currentPosition = scenario.Fixture.Database.Store.Position;
        var repeated = await client.CommitAsync(healthy, token);
        await Assert.That(repeated.IsSuccess).IsTrue();
        await CommittedResponseAssertions.ReceiptAsync(repeated.Value, expectedHealthy);
        await CommittedResponseAssertions.UnchangedAsync(scenario, current, currentPosition);
        var replies = endpoint.Replies.ToArray();
        await Assert.That(replies.Length).IsEqualTo(CommittedResponseProtocol.SuccessfulRequests);
        await Assert.That(endpoint.OriginalReply).IsNotNull();
        var originalReply = endpoint.OriginalReply
            ?? throw new InvalidOperationException(CommittedResponseProtocol.UnexpectedRequest);
        await Assert.That(replies[CommittedResponseProtocol.InitialIndex].SequenceEqual(originalReply)).IsTrue();
        await Assert.That(replies[CommittedResponseProtocol.ReplayIndex].SequenceEqual(originalReply)).IsTrue();
        await Assert.That(replies[CommittedResponseProtocol.HealthyIndex].SequenceEqual(replies[CommittedResponseProtocol.HealthyReplayIndex])).IsTrue();
        await Assert.That(endpoint.Requests.Count).IsEqualTo(CommittedResponseProtocol.MaximumRequests);
        CommandRequest[] expectedRequests = [command, command, changed, healthy, healthy];
        var actualRequests = endpoint.Requests.ToArray();
        for (var index = CommittedResponseProtocol.InitialIndex; index < expectedRequests.Length; index++)
        {
            await Assert.That(JsonDefaults.Serialize(actualRequests[index]).SequenceEqual(JsonDefaults.Serialize(expectedRequests[index]))).IsTrue();
        }
    }
}
