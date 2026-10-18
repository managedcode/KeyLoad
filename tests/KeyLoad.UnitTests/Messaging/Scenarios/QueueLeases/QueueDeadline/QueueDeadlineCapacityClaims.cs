using KeyLoad.Core;
using KeyLoad.UnitTests.Features.ResourceExecution;
using ManagedCode.Communication;
using static KeyLoad.UnitTests.Features.Messaging.QueueDeadlineCapacityProtocol;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueDeadlineCapacityClaims
{
    internal static ReceiveResult Expected(DatabaseEngine database, QueueLaneRef lane, MessageMetadata metadata,
        Guid requestId, CommitToken commit, DateTimeOffset time)
    {
        if (metadata.State != MessageState.Ready)
        {
            return new(requestId, [], commit);
        }
        var claims = new DeliveryClaims(lane, metadata.Id, Root, metadata.LeaseVersion + LeaseVersionStep,
            metadata.DeliveryGeneration, commit.Incarnation);
        return new(requestId, [new(metadata.Id, QueueDeadlineCapacityProtocol.Json, QueueDeadlineCapacityProtocol.Json, database.Sign(claims), claims.LeaseVersion,
            time.AddSeconds(LeaseSeconds), metadata.Attempts + AttemptStep, metadata.DeliveryGeneration)], commit);
    }

    internal static MessageMetadata Leased(MessageMetadata metadata, DateTimeOffset time)
        => metadata with
        {
            State = MessageState.Leased,
            Attempts = metadata.Attempts + AttemptStep,
            StateVersion = metadata.StateVersion + StateVersionStep,
            LeaseVersion = metadata.LeaseVersion + LeaseVersionStep,
            LeaseOwner = Root,
            LeaseUntil = time.AddSeconds(LeaseSeconds)
        };

    internal static async Task RequireAsync(DatabaseEngine database, ReceiveResult actual, ReceiveResult expected, QueueLaneRef lane)
    {
        await NativeReplayResultAssertions.Same<ReceiveResult>(Result<object>.Succeed(actual), Result<object>.Succeed(expected));
        foreach (var delivery in expected.Deliveries)
        {
            await Assert.That(database.Verify<DeliveryClaims>(delivery.Token)).IsEqualTo(new DeliveryClaims(lane,
                delivery.Id, Root, delivery.LeaseVersion, delivery.DeliveryGeneration, expected.Token.Incarnation));
        }
    }
}
