using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.UnitTests.Features.ResourceExecution;
using ManagedCode.Communication;
using static KeyLoad.UnitTests.Features.EventStreams.EventQueueStateEnvelopeProtocol;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal static class EventQueueStateEnvelopeCommands
{
    internal static CommitToken NextToken(EventQueueStateEnvelopeFixture fixture)
    {
        var placement = fixture.Database.ReadAtomicPartitionPlacement(Root, fixture.Partition);
        return new(placement.Incarnation, fixture.Partition.AtomicPartitionId,
            checked(fixture.Store.Position + CommitPositionStep));
    }

    internal static async Task<EventQueueStateEnvelopeHistory> SubmitAsync<TRequest, TResult>(
        EventQueueStateEnvelopeFixture fixture, EventQueueStateEnvelopeState state, OperationKind kind,
        Guid id, TRequest request, DateTimeOffset time, TResult expected)
        where TResult : class
    {
        var operation = fixture.Database.OwnCommand(fixture.Database.CreateCommand(kind, id, Root, time, JsonSerializer.Serialize(request, JsonDefaults.Options)));
        var result = fixture.Database.Apply(operation);
        await NativeReplayResultAssertions.Same<TResult>(result, Result<object>.Succeed(expected));
        var outcome = Required(fixture, KeySpace.PartitionOutcome(fixture.Partition, Root, id));
        var stored = NativeSerialization.Deserialize<StoredOutcome>(outcome);
        await Assert.That(stored.Incarnation).IsEqualTo(fixture.Store.Identity.Incarnation);
        await Assert.That(stored.PolicyEpoch).IsEqualTo(InitialPolicyEpoch);
        await Assert.That(stored.ScopeKind).IsEqualTo(CommandOutcomeScopeKind.Partition);
        await Assert.That(stored.Partition).IsEqualTo(fixture.Partition);
        await NativeReplayResultAssertions.Same<TResult>(stored.Result, result);
        var locator = Required(fixture, KeySpace.OutcomeLocatorV2(fixture.Partition, Root, id));
        await Assert.That(locator.AsSpan().SequenceEqual(KeySpace.PartitionOutcome(fixture.Partition, Root, id))).IsTrue();
        var retained = new EventQueueStateEnvelopeHistory(operation, result, outcome, locator);
        state.History.Add(retained);
        return retained;
    }

    internal static Task<EventQueueStateEnvelopeHistory> CommitAsync(EventQueueStateEnvelopeFixture fixture,
        EventQueueStateEnvelopeState state, ImmutableArray<Mutation> effects,
        ImmutableArray<MutationReceipt> expectedEffects, DateTimeOffset time)
    {
        var id = Guid.NewGuid();
        var command = new CommandRequest(id, fixture.Partition, effects);
        var expected = new CommitReceipt(id, NextToken(fixture), expectedEffects, DurabilityProfile.ProcessDurable, 1);
        return SubmitAsync(fixture, state, OperationKind.Batch, id, command, time, expected);
    }

    internal static byte[] Required(EventQueueStateEnvelopeFixture fixture, byte[] key)
    {
        return fixture.Store.Read(view => view.ReadOwnedValue(key)) ?? throw new InvalidOperationException(MissingRow);
    }
}
