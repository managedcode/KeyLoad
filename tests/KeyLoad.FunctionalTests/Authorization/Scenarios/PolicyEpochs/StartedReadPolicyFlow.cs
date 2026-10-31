using KeyLoad.Clustering;
using KeyLoad.FunctionalTests.Messaging;
using KeyLoad.FunctionalTests.OrleansExecution;
using KeyLoad.Server;
using P = KeyLoad.FunctionalTests.Authorization.StartedReadPolicyProtocol;

namespace KeyLoad.FunctionalTests.Authorization;

internal static class StartedReadPolicyFlow
{
    internal static Task RunAsync(string variant)
        => ConnectionScenario.RunAsync(async scenario =>
        {
            await scenario.InitializeAsync(TestContext.Current!.Execution.CancellationToken);
            using var timeout = new CancellationTokenSource(scenario.Timing.CompletionTimeout, TimeProvider.System);
            using var parent = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, TestContext.Current!.Execution.CancellationToken);
            var token = parent.Token;
            var writer = scenario.Principal();
            var reader = scenario.Principal();
            var command = scenario.Command(P.Document);
            var expectedToken = ExpectedCanonicalCommit.Next(scenario.Fixture.Database.Database, command.Partition);
            var receipt = await scenario.CommandAsync(writer, Guid.NewGuid(), command, token);
            await StartedReadPolicyAssertions.ReceiptAsync(scenario, command, receipt, expectedToken);
            var outcome = scenario.Fixture.Database.Store.Read(view => view.ReadOwnedValue(
                OutcomeStoreOracle.PartitionKey(command.Partition, writer.Id, command.CommandId)))!;
            await HeldAsync(scenario, writer, reader, command, receipt, outcome, variant, token);
            await scenario.CloseAsync(token);
        });

    private static async Task HeldAsync(ConnectionScenario scenario, PrincipalRecord writer,
        PrincipalRecord reader, CommandRequest command, GrainOperationReply receipt, byte[] outcome,
        string variant, CancellationToken token)
    {
        using var selected = CancellationTokenSource.CreateLinkedTokenSource(token);
        var requestId = Guid.NewGuid();
        var gate = scenario.Observation.Hold(requestId);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var original = StartedReadPolicyConsumer.RunAsync(scenario, reader, requestId, started, selected.Token, token);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await Task.WhenAny(started.Task, original).WaitAsync(token);
            if (original.IsCompleted)
            {
                await original;
            }
            await started.Task.WaitAsync(token);
            await gate.Arrived.Task.WaitAsync(token);
            await ContinueAsync(scenario, writer, reader, command, receipt, outcome, variant,
                requestId, gate, original, selected, token);
        }, failures);
        await ServerFailureObserver.ObserveAsync(selected.CancelAsync, failures);
        gate.Release.TrySetResult();
        using var cleanup = new CancellationTokenSource(scenario.Timing.ShutdownTimeout, TimeProvider.System);
        await ServerFailureObserver.ObserveAsync(async () => { await original.WaitAsync(cleanup.Token); }, failures);
        await ServerFailureObserver.ObserveAsync(() => scenario.Observation.Read(requestId).Disposed.Task.WaitAsync(cleanup.Token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ContinueAsync(ConnectionScenario scenario, PrincipalRecord writer,
        PrincipalRecord reader, CommandRequest command, GrainOperationReply receipt, byte[] outcome,
        string variant, Guid requestId, ConnectionOperationGate gate, Task<StartedReadPolicyObservation> original,
        CancellationTokenSource selected, CancellationToken token)
    {
        if (variant != P.Cancelled)
        {
            await PersistPrincipalAsync(scenario, reader with
            {
                Revoked = variant == P.Revoked,
                ExpiresAt = variant == P.Expired ? DateTimeOffset.UnixEpoch : null,
                PolicyEpoch = P.RefusedEpoch
            });
        }
        var siblingId = Guid.NewGuid();
        await StartedReadPolicyAssertions.DocumentAsync(scenario, await scenario.ReadAsync(writer, siblingId, P.Document, token));
        await Assert.That(original.IsCompleted).IsFalse();
        var image = QueueWholeFlowStorage.Bytes(scenario.Fixture.Database.Store);
        var position = scenario.Fixture.Database.Store.Position;
        if (variant == P.Cancelled)
        {
            await selected.CancelAsync();
        }
        else
        {
            gate.Release.TrySetResult();
        }
        await StartedReadPolicyAssertions.FramesAsync(await original.WaitAsync(token), requestId, variant);
        await scenario.Observation.Read(requestId).Disposed.Task.WaitAsync(token);
        await StartedReadPolicyAssertions.ConservedAsync(scenario, image, position, outcome, writer.Id, command);
        await ConnectionAssertions.SameOwnerAsync(scenario, requestId, siblingId, token);
        await Assert.That(scenario.Observation.Read(requestId).Identity.PrincipalId).IsEqualTo(reader.Id);
        await Assert.That(scenario.Observation.Read(siblingId).Identity.PrincipalId).IsEqualTo(writer.Id);
        if (variant != P.Cancelled)
        {
            await PersistPrincipalAsync(scenario, reader with
            {
                PolicyEpoch = P.RepairedEpoch
            });
        }
        var healthyId = Guid.NewGuid();
        await StartedReadPolicyAssertions.DocumentAsync(scenario, await scenario.ReadAsync(reader, healthyId, P.Document, token));
        var replay = await scenario.CommandAsync(writer, Guid.NewGuid(), command, token);
        await Assert.That(replay.Error).IsNull();
        await Assert.That(OrleansSerialization.Serialize(GrainReplyFactory.RequireValue(replay)).AsSpan().SequenceEqual(OrleansSerialization.Serialize(GrainReplyFactory.RequireValue(receipt)))).IsTrue();
        await ConnectionAssertions.SameOwnerAsync(scenario, requestId, healthyId, token);
        var current = scenario.Fixture.Database.Store.Read(view => view.ReadOwnedValue(
            OutcomeStoreOracle.PartitionKey(command.Partition, writer.Id, command.CommandId)))!;
        await Assert.That(current.AsSpan().SequenceEqual(outcome)).IsTrue();
    }

    private static async Task PersistPrincipalAsync(ConnectionScenario scenario, PrincipalRecord principal)
    {
        var acknowledged = scenario.Fixture.Database.Submit(OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(principal)).Get<PrincipalRecord>();
        await StartedReadPolicyAssertions.EqualAsync(principal, acknowledged);
    }
}
