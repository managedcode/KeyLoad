using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.DocumentStorage;
using KeyLoad.Query.Features.QueryExecution;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.DocumentStorage;
using KeyLoad.Server.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

/// <summary>Inspects only the existing bounded native witness of the actual original public operation.</summary>
internal static class QueryLifetimeNativeWitness
{
    internal static async Task RequireAsync(RequestCqrsProbeFixture controls, RequestCqrsProbeMarkerRecord marker,
        RemoteResourceLifetimeReadKindV2 kind, string principalId)
    {
        var node = RequestCqrsProbeFixtureProtocol.Nodes.Single(value =>
            RequestCqrsProbeFileNames.OriginForNode(value) == marker.Voter);
        var owner = controls.NodeFor(node);
        RequestCqrsProbeFileStore.VerifyOwnerFile(owner.Directory, owner.OwnerBytes);
        var name = RequestCqrsNativeModelInventory.Name(marker.ArmId, marker.RequestId, marker.Phase);
        var witness = RequestCqrsNativeModelInventory.Read(RequestCqrsProbeFileStore.ReadRecord(Path.Combine(owner.Directory, name)));
        await Assert.That(witness.SessionId).IsEqualTo(controls.SessionId);
        await Assert.That(witness.RequestId).IsEqualTo(marker.RequestId);
        await Assert.That(witness.CommandId).IsEqualTo(Guid.Empty);
        await Assert.That(witness.ConnectionId).IsNotEqualTo(Guid.Empty);
        var native = RequestCqrsNativeModelInventory.Request(witness);
        var reply = RequestCqrsNativeModelInventory.Reply(witness);
        await Assert.That(native.Channel).IsEqualTo(RemoteModelNativeChannel.ResourceLifetimeRead);
        await Assert.That(reply.Error).IsNull();
        var call = NativeSerialization.Deserialize<RemoteResourceLifetimeReadCallV2>(native.Payload.Span);
        var result = NativeSerialization.Deserialize<RemoteResourceLifetimeReadReplyV2>(reply.Payload.Span);
        var request = NativeSerialization.Deserialize<RemoteQueryLifetimeRequestV2>(call.Payload.Span);
        await Assert.That(call.Kind).IsEqualTo(kind);
        await Assert.That(call.RequestId).IsEqualTo(request.Original.RequestId);
        await Assert.That(call.RequestId).IsNotEqualTo(marker.RequestId);
        await Assert.That(call.Nonce).IsEqualTo(request.Original.Nonce);
        await Assert.That(call.ExpiresAt).IsEqualTo(native.ExpiresAt);
        await Assert.That(call.ExpiresAt).IsEqualTo(request.Original.ExpiresAt);
        await Assert.That(request.Principal.Id).IsEqualTo(principalId);
        await Assert.That(request.Principal.PolicyEpoch).IsEqualTo(request.Original.Fence.PolicyEpoch);
        await Assert.That(result.RequestId).IsEqualTo(call.RequestId);
        await Assert.That(result.Nonce).IsEqualTo(call.Nonce);
        await Assert.That(result.Error).IsNull();
        PartitionControlLifetimeCoverage.Require(request.Coverage, RemoteDocumentRf3Protocol.Partition,
            [request.Definition], RemoteDocumentProtocol.MaximumBodyBytes);
        await Assert.That(request.Definition.Name).IsEqualTo(RemoteDocumentRf3Protocol.Collection);
        if (kind == RemoteResourceLifetimeReadKindV2.DistributedSearch)
        {
            await Assert.That(request.Original.SearchLeaf!.Phase).IsEqualTo(DistributedSearchPhase.Statistics);
        }
        else
        {
            var leaf = NativeSerialization.Deserialize<PartitionQueryLeafResultV1>(result.Payload.Span);
            await Assert.That(leaf.Candidates.Single().Reference).IsEqualTo(RemoteDocumentRf3Protocol.Reference);
            await Assert.That(leaf.Candidates.Single().Row.Revision).IsEqualTo(RemoteDocumentRf3Protocol.FirstRevision);
            await Assert.That(leaf.Candidates.Single().Row.Json).IsEqualTo(RemoteDocumentRf3Protocol.ProjectedJson);
        }
    }
}
