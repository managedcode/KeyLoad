using System.Security.Claims;
using KeyLoad.Clustering;
using KeyLoad.Server;
using KeyLoad.UnitTests.Authorization;
using KeyLoad.UnitTests.Messaging;
using ManagedCode.Orleans.Identity.Core.Constants;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.OrleansExecution;

internal static class CallLocalIdentityRestorationFlow
{
    private const string FirstDocument = "context-cancelled-first";
    private const string SecondDocument = "context-completed-second";
    private const string PutKind = "putDocument";

    internal static Task RunAsync(bool firstPresentNull, CancellationToken token)
        => ConnectionScenario.RunAsync(scenario => ExerciseAsync(scenario, firstPresentNull, token));

    private static async Task ExerciseAsync(ConnectionScenario scenario, bool firstPresentNull,
        CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(scenario.Timing.CompletionTimeout, TimeProvider.System);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);
        var token = linked.Token;
        await scenario.InitializeAsync(token);
        var first = scenario.Principal();
        var second = scenario.Principal();
        await Assert.That(first.Id).IsNotEqualTo(second.Id);
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var held = scenario.Observation.Hold(firstId);
        var command = scenario.Command(FirstDocument, expectedRevision: 0);
        using var selected = CancellationTokenSource.CreateLinkedTokenSource(token);
        var original = InvokeAsync(scenario, first, firstId, command, firstPresentNull, selected.Token);
        Exception? primary = null;
        try
        {
            await held.Arrived.Task.WaitAsync(token);
            var sibling = scenario.Command(SecondDocument, ConnectionProtocol.SecondJson, expectedRevision: 0);
            var expected = NextToken(scenario);
            var reply = await InvokeAsync(scenario, second, secondId, sibling, !firstPresentNull, token);
            await ReceiptAsync(scenario, sibling, expected, reply, ConnectionProtocol.SecondJson);
            await Assert.That(original.IsCompleted).IsFalse();
            var position = scenario.Fixture.Database.Store.Position;
            var image = QueueWholeFlowStorage.Bytes(scenario.Fixture.Database.Store);
            await selected.CancelAsync();
            await ConnectionAssertions.CancelledAsync(original);
            await scenario.Observation.Read(firstId).Disposed.Task.WaitAsync(token);
            await ConnectionAssertions.MissingOutcomeAsync(scenario, first, command, FirstDocument);
            await Assert.That(scenario.Fixture.Database.Store.Position).IsEqualTo(position);
            await Assert.That(QueueWholeFlowStorage.Bytes(scenario.Fixture.Database.Store)
                .SequenceEqual(image, StringComparer.Ordinal)).IsTrue();
            await SameIdentityAsync(scenario, firstId, first, command.CommandId);
            await SameIdentityAsync(scenario, secondId, second, sibling.CommandId);
            await ConnectionAssertions.SameOwnerAsync(scenario, firstId, secondId, token);
            await FollowingAsync(scenario, first, command, firstId, firstPresentNull,
                Outbox(scenario, sibling, expected, ConnectionProtocol.SecondJson, 1), token);
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            await selected.CancelAsync();
            held.Release.TrySetResult();
            await ConnectionAwaitedSettlementCalls.JoinOriginalAsync(original, primary, allowCancellation: true);
        }
        await scenario.CloseAsync(token);
    }

    private static async Task FollowingAsync(ConnectionScenario scenario, PrincipalRecord first,
        CommandRequest command, Guid cancelledId, bool presentNull, OutboxEntry sibling, CancellationToken token)
    {
        var requestId = Guid.NewGuid();
        var expected = NextToken(scenario);
        var reply = await InvokeAsync(scenario, first, requestId, command, presentNull, token);
        await ReceiptAsync(scenario, command, expected, reply, ConnectionProtocol.FirstJson);
        await SameIdentityAsync(scenario, requestId, first, command.CommandId);
        await ConnectionAssertions.SameOwnerAsync(scenario, cancelledId, requestId, token);
        await SubscriptionOldCutOutbox.EntriesAsync(scenario.Fixture.Database.Database,
            scenario.Fixture.Database.Partition,
            [sibling, Outbox(scenario, command, expected, ConnectionProtocol.FirstJson, 2)]);
    }

    private static OutboxEntry Outbox(ConnectionScenario scenario, CommandRequest command,
        CommitToken token, string json, long sequence)
    {
        var put = (PutDocument)command.Mutations.Single();
        var retained = scenario.Fixture.Database.ReadStoredOutcome(command.CommandId);
        var time = new DateTimeOffset(retained.PreparedCommand!.Timestamp);
        return new(sequence, 0, token, time, put,
            new(PutKind, ConnectionProtocol.Collection, put.Id, 1),
            After: new(scenario.Reference(put.Id), 1, json, new RowAccess(), time));
    }

    private static CommitToken NextToken(ConnectionScenario scenario)
        => new(scenario.Fixture.Database.Store.Identity.Incarnation,
            scenario.Fixture.Database.Partition.AtomicPartitionId,
            checked(scenario.Fixture.Database.ReadCanonicalCut().AppliedPosition + 1));

    private static async Task ReceiptAsync(ConnectionScenario scenario, CommandRequest command,
        CommitToken token, GrainOperationReply reply, string json)
    {
        await Assert.That(reply.Error).IsNull();
        var id = ((PutDocument)command.Mutations.Single()).Id;
        var expected = new CommitReceipt(command.CommandId, token, [new(PutKind, ConnectionProtocol.Collection, id, 1)], DurabilityProfile.ProcessDurable,
            scenario.Fixture.Source.Services.GetRequiredService<IOptions<NodeOptions>>().Value.ReplicaCount);
        await SafeProjectorOperationSeed.EqualAsync(ConnectionAssertions.Value<CommitReceipt>(reply), expected);
        await SafeProjectorOperationSeed.EqualAsync(scenario.Stored(id),
            new DocumentResult(scenario.Reference(id), 1, json, false, []));
        var entry = scenario.Fixture.Database.ReadStoredOutcome(command.CommandId);
        await Assert.That(entry.CanonicalSequence).IsEqualTo(token.Position);
        await Assert.That(entry.PreparedCommand!.CommandId).IsEqualTo(command.CommandId);
    }

    private static async Task SameIdentityAsync(ConnectionScenario scenario, Guid requestId,
        PrincipalRecord principal, Guid commandId)
    {
        var observed = scenario.Observation.Read(requestId).Identity;
        await Assert.That(observed.PrincipalId).IsEqualTo(principal.Id);
        await Assert.That(observed.RequestId).IsEqualTo(requestId);
        await Assert.That(observed.CommandId).IsEqualTo(commandId);
    }

    private static async Task<GrainOperationReply> InvokeAsync(ConnectionScenario scenario,
        PrincipalRecord principal, Guid requestId, CommandRequest command, bool presentNull, CancellationToken token)
    {
        var user = RequestCqrsContextSlot.Capture(OrleansIdentityConstants.USER_CLAIMS);
        var state = RequestCqrsContextSlot.Capture(GrainRequestStreamProtocol.ContextKey);
        var prior = presentNull ? RequestCqrsPriorContextValue.PresentNull : RequestCqrsPriorContextValue.Object;
        var oldUser = new ClaimsPrincipal();
        var oldState = new GrainRequestContextState(Guid.NewGuid(), Guid.NewGuid(), scenario.Fixture.ConnectionId);
        var failures = new List<Exception>();
        GrainOperationReply? reply = null;
        try
        {
            user.SetPrior(prior, oldUser);
            state.SetPrior(prior, oldState);
            await ServerFailureObserver.ObserveAsync(async () => reply = await ConnectionAwaitedSettlementCalls.CommandAsync(
                scenario, principal, requestId, command, token), failures);
            await ServerFailureObserver.ObserveAsync(() => user.AssertPriorAsync(prior, oldUser), failures);
            await ServerFailureObserver.ObserveAsync(() => state.AssertPriorAsync(prior, oldState), failures);
        }
        finally
        {
            user.Restore();
            state.Restore();
        }
        ServerFailureObserver.ThrowIfAny(failures);
        return reply!;
    }
}
