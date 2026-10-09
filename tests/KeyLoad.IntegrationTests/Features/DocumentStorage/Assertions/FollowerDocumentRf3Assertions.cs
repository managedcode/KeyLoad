using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

internal static class FollowerDocumentRf3Assertions
{
    internal static async Task FollowerStatusAsync(NodeStatus status, string replica)
    {
        await Assert.That(status.RoutingReady).IsTrue();
        await Assert.That(status.Voters).IsEqualTo(RequestCqrsRf3Protocol.NodeCount);
        await Assert.That(status.Leader).IsNotNull();
        await Assert.That(status.Leader).IsNotEqualTo(replica);
        await Assert.That(status.ConsensusTerm).IsGreaterThan(0);
        await Assert.That(status.Applied).IsGreaterThan(0);
    }

    internal static async Task FullReceiptAsync(CommitReceipt actual, CommandRequest command,
        AtomicPartitionPlacementResolution owner)
    {
        var expected = new CommitReceipt(command.CommandId,
            new(owner.Incarnation, command.Partition.AtomicPartitionId, actual.Token.Position, owner.PlacementEpoch),
            [new("putDocument", RequestCqrsRf3Protocol.AdminCollection, RequestCqrsRf3Protocol.DocumentId,
                FollowerDocumentRf3Protocol.SecondRevision)], DurabilityProfile.QuorumProcessDurable);
        await Assert.That(actual.Token.Position).IsGreaterThan(0);
        await Assert.That(JsonDefaults.Serialize(actual).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }

    internal static async Task FullAsync(FollowerDocumentPublicObservation observation, ReadFollowerDocumentRequestV1 request,
        AtomicPartitionPlacementResolution owner, NodeStatus dataLower, NodeStatus authorityLower, string json,
        long revision, long policyEpoch, bool redacted, NodeStatus? upper = null)
    {
        await Assert.That(observation.IsSuccess).IsTrue();
        await Assert.That(observation.Failure).IsNull();
        await Assert.That(observation.Problem).IsNull();
        await Assert.That(observation.Value).IsNotNull();
        var actual = observation.Value!;
        await Assert.That(actual.DataToken.Position).IsGreaterThanOrEqualTo(dataLower.Applied);
        await Assert.That(actual.AuthorizationToken.Position).IsGreaterThanOrEqualTo(authorityLower.Applied);
        await Assert.That(actual.DataToken.Position).IsLessThanOrEqualTo(actual.AuthorizationToken.Position);
        if (upper is null)
        { await Assert.That(actual.DataToken.Position).IsLessThan(authorityLower.Applied); }
        else
        {
            await Assert.That(actual.AuthorizationToken.Position).IsLessThanOrEqualTo(upper.Applied);
            await Assert.That(upper.NodeId).IsEqualTo(dataLower.NodeId);
            await Assert.That(upper.ConsensusTerm).IsEqualTo(dataLower.ConsensusTerm);
        }
        await Assert.That(authorityLower.NodeId).IsEqualTo(dataLower.NodeId);
        await Assert.That(authorityLower.ReadGeneration).IsEqualTo(dataLower.ReadGeneration);
        await Assert.That(actual.DataToken.Position).IsGreaterThanOrEqualTo(request.MinimumToken!.Position);
        var data = new CommitToken(owner.Incarnation, request.Reference.Partition.AtomicPartitionId,
            actual.DataToken.Position, owner.PlacementEpoch);
        var authority = data with { Position = actual.AuthorizationToken.Position };
        var expected = new FollowerDocumentReadResultV1(FollowerDocumentRf3Protocol.Version,
            DocumentFollowerReadMode.FollowerCommittedSnapshot, request.ReplicaId, dataLower.ConsensusTerm,
            authorityLower.ConsensusTerm, data, authority, authority.Position - data.Position, policyEpoch,
            new(request.Reference, revision, json, redacted, redacted ? [FollowerDocumentRf3Protocol.ProtectedPath] : []));
        await Assert.That(JsonDefaults.Serialize(actual).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(actual.PositionLag).IsLessThanOrEqualTo(request.MaximumLagPositions);
    }

    internal static async Task RejectedAsync(FollowerDocumentPublicObservation observation, ErrorCode code, string secret)
    {
        await Assert.That(observation.IsSuccess).IsFalse();
        await Assert.That(observation.Value).IsNull();
        await Assert.That(observation.Failure).IsNull();
        if (observation.SqlValueKind is { } kind)
        { await Assert.That(kind).IsEqualTo(System.Text.Json.JsonValueKind.Undefined); }
        if (observation.Official is { } official)
        {
            await McpCallerAssertions.ErrorAsync(official, code, dispatched: true);
            await McpCallerAssertions.DoesNotDiscloseAsync(official, secret, RequestCqrsRf3Protocol.DocumentJson);
            await McpCallerAssertions.DoesNotDiscloseAsync(official, secret, FollowerDocumentRf3Protocol.PrivateMarker);
        }
        else
        {
            await Assert.That(observation.Problem).IsNotNull();
            await Assert.That(observation.Problem!.ErrorCode).IsEqualTo(code.ToString());
            await Assert.That(observation.Problem.StatusCode).IsEqualTo(Errors.Status(code));
            await Assert.That(observation.Problem.Detail?.Contains(secret, StringComparison.Ordinal) is true).IsFalse();
            await Assert.That(observation.Problem.Detail?.Contains(RequestCqrsRf3Protocol.DocumentJson, StringComparison.Ordinal) is true).IsFalse();
            await Assert.That(observation.Problem.Detail?.Contains(FollowerDocumentRf3Protocol.PrivateMarker, StringComparison.Ordinal) is true).IsFalse();
        }
    }

    internal static async Task CancelledAsync(FollowerDocumentPublicObservation observation, string secret, FollowerDocumentCaller mode,
        CancellationToken token)
    {
        await Assert.That(token.IsCancellationRequested).IsTrue();
        await Assert.That(observation.IsSuccess).IsFalse();
        await Assert.That(observation.Value).IsNull();
        if (observation.Failure is OperationCanceledException failure)
        {
            await Assert.That(failure.CancellationToken.IsCancellationRequested).IsTrue();
            await Assert.That(observation.Problem).IsNull();
            await Assert.That(observation.Official).IsNull();
            return;
        }
        var code = mode == FollowerDocumentCaller.SqlSdk ? ErrorCode.UnknownWriteOutcome : ErrorCode.Cancelled;
        await RejectedAsync(observation, code, secret);
    }
}
