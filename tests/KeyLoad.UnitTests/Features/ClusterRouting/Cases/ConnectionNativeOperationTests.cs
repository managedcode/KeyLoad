namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class ConnectionNativeOperationTests
{
    [Test]
    public Task AcCrs050SignedCreateReadUpdateAndReplayReuseOneActualActivation()
    {
        return ConnectionNativeScenario.RunAsync(async scenario =>
        {
            using var timeout = new CancellationTokenSource(scenario.Timing.CompletionTimeout, TimeProvider.System);
            var token = timeout.Token;
            await scenario.InitializeAsync(token);
            var principal = scenario.Principal();
            var createId = Guid.NewGuid();
            var command = scenario.Command(nameof(AcCrs050SignedCreateReadUpdateAndReplayReuseOneActualActivation));
            var created = await scenario.CommandAsync(principal, createId, command, token);
            await Assert.That(created.Error).IsNull();
            var receipt = ConnectionNativeAssertions.Value<CommitReceipt>(created);
            await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
            var documentId = ((PutDocument)command.Mutations.Single()).Id;
            await ConnectionNativeAssertions.StoredAsync(scenario, documentId, 1, ConnectionNativeProtocol.FirstJson);
            var readId = Guid.NewGuid();
            var read = await scenario.ReadAsync(principal, readId, documentId, token);
            await Assert.That(read.Error).IsNull();
            var document = ConnectionNativeAssertions.Value<DocumentResult>(read);
            await Assert.That(document.Json).IsEqualTo(ConnectionNativeProtocol.FirstJson);
            await Assert.That(document.Reference).IsEqualTo(scenario.Reference(documentId));
            var retryId = Guid.NewGuid();
            var retried = await scenario.CommandAsync(principal, retryId, command, token);
            await Assert.That(retried.Error).IsNull();
            await Assert.That(created.Payload.Span.SequenceEqual(retried.Payload.Span)).IsTrue();
            await ConnectionNativeAssertions.StoredAsync(scenario, documentId, 1, ConnectionNativeProtocol.FirstJson);
            var updateId = Guid.NewGuid();
            var update = scenario.Command(documentId, ConnectionNativeProtocol.SecondJson, expectedRevision: 1);
            var updated = await scenario.CommandAsync(principal, updateId, update, token);
            await Assert.That(updated.Error).IsNull();
            await Assert.That(ConnectionNativeAssertions.Value<CommitReceipt>(updated).CommandId).IsEqualTo(update.CommandId);
            await ConnectionNativeAssertions.StoredAsync(scenario, documentId, 2, ConnectionNativeProtocol.SecondJson);
            await ConnectionNativeAssertions.SameOwnerAsync(scenario, createId, readId, token);
            await ConnectionNativeAssertions.SameOwnerAsync(scenario, createId, retryId, token);
            await ConnectionNativeAssertions.SameOwnerAsync(scenario, createId, updateId, token);
            await scenario.CloseAsync(token);
        });
    }

    [Test]
    public Task AcCrs051HeldCommandAllowsAnotherPrincipalAndCancellationIsIsolated()
    {
        return ConnectionNativeScenario.RunAsync(async scenario =>
        {
            using var timeout = new CancellationTokenSource(scenario.Timing.CompletionTimeout, TimeProvider.System);
            var token = timeout.Token;
            await scenario.InitializeAsync(token);
            var first = scenario.Principal();
            var second = scenario.Principal();
            var firstId = Guid.NewGuid();
            var held = scenario.Observation.Hold(firstId);
            var firstCommand = scenario.Command("cancelled-first");
            using var selectedCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            var firstCall = scenario.CommandAsync(first, firstId, firstCommand, selectedCancellation.Token);
            await held.Arrived.Task.WaitAsync(token);
            var secondId = Guid.NewGuid();
            var secondCommand = scenario.Command("completed-second", ConnectionNativeProtocol.SecondJson);
            var secondReply = await scenario.CommandAsync(second, secondId, secondCommand, token);
            await Assert.That(secondReply.Error).IsNull();
            await Assert.That(firstCall.IsCompleted).IsFalse();
            await ConnectionNativeAssertions.StoredAsync(scenario, "completed-second", 1, ConnectionNativeProtocol.SecondJson);
            await selectedCancellation.CancelAsync();
            await ConnectionNativeAssertions.CancelledAsync(firstCall);
            await scenario.Observation.Read(firstId).Disposed.Task.WaitAsync(token);
            await ConnectionNativeAssertions.MissingOutcomeAsync(scenario, first, firstCommand, "cancelled-first");
            await ConnectionNativeAssertions.SameOwnerAsync(scenario, firstId, secondId, token);
            await Assert.That(scenario.Observation.Read(firstId).Identity.PrincipalId).IsEqualTo(first.Id);
            await Assert.That(scenario.Observation.Read(secondId).Identity.PrincipalId).IsEqualTo(second.Id);
            var read = await scenario.ReadAsync(second, Guid.NewGuid(), "completed-second", token);
            await Assert.That(ConnectionNativeAssertions.Value<DocumentResult>(read).Json)
                .IsEqualTo(ConnectionNativeProtocol.SecondJson);
            await scenario.CloseAsync(token);
        });
    }
}
