using KeyLoad.Orleans;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class ConnectionNativeAssertions
{
    internal static T Value<T>(GrainOperationReply reply)
        => NativeSerialization.Deserialize<GrainValue>(reply.Payload.Span).Value is T result ? result
            : throw new InvalidOperationException(ConnectionNativeProtocol.UnexpectedReply);

    internal static async Task StoredAsync(ConnectionNativeScenario scenario, string id, long revision, string json)
    {
        var document = scenario.Stored(id);
        await Assert.That(document).IsNotNull();
        await Assert.That(document!.Revision).IsEqualTo(revision);
        await Assert.That(document.Json).IsEqualTo(json);
        await Assert.That(document.Reference).IsEqualTo(scenario.Reference(id));
    }

    internal static async Task SameOwnerAsync(ConnectionNativeScenario scenario, Guid first, Guid second,
        CancellationToken cancellationToken)
    {
        var initial = scenario.Observation.Read(first);
        var following = scenario.Observation.Read(second);
        await initial.Disposed.Task.WaitAsync(cancellationToken);
        await following.Disposed.Task.WaitAsync(cancellationToken);
        await Assert.That(initial.GrainId).IsEqualTo(following.GrainId);
        await Assert.That(initial.ActivationId).IsEqualTo(following.ActivationId);
        await Assert.That(initial.ActivationId.IsDefault).IsFalse();
        await Assert.That(initial.Identity.RequestId).IsNotEqualTo(following.Identity.RequestId);
        await scenario.AssertActivationCountAsync(ConnectionNativeProtocol.OneConnection, cancellationToken);
    }

    internal static async Task CancelledAsync(Task<GrainOperationReply> operation)
    {
        try
        {
            var reply = await operation;
            await Assert.That(reply.Error is ErrorCode.Cancelled or ErrorCode.UnknownWriteOutcome).IsTrue();
        }
        catch (OperationCanceledException)
        { }
    }

    internal static async Task MissingOutcomeAsync(ConnectionNativeScenario scenario, PrincipalRecord principal,
        CommandRequest command, string id)
    {
        await Assert.That(scenario.Stored(id)).IsNull();
        await Assert.That(OutcomeStoreOracle.ReadPartition(scenario.Fixture.Database.Store,
            scenario.Fixture.Database.Partition, principal.Id, command.CommandId)).IsNull();
    }
}
