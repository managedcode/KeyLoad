using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.DocumentStorage;
using KeyLoad.Server.Features.Search;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class AnnLifetimeRf3Witness
{
    private const int EqualKeyOrder = 0;
    internal static async Task RequireAsync(RequestCqrsProbeFixture controls, RequestCqrsProbeMarkerRecord marker,
        RemoteAnnPublicationRf3State state, string principalId, GrainReadKind kind)
    {
        var node = RequestCqrsProbeFixtureProtocol.Nodes.Single(value => RequestCqrsProbeFileNames.OriginForNode(value) == marker.Voter);
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
        await Assert.That(native.Channel).IsEqualTo(RemoteModelNativeChannel.ResourceLifetimeAnnRead);
        await Assert.That(reply.Error).IsNull();
        var call = NativeSerialization.Deserialize<AnnProjectionNativeCallV2>(native.Payload.Span);
        var result = NativeSerialization.Deserialize<AnnProjectionObservationResult>(reply.Payload.Span);
        await Assert.That(call.Principal.Id).IsEqualTo(principalId);
        await Assert.That(call.Original.PrincipalId).IsEqualTo(principalId);
        await Assert.That(call.Original.ExpiresAt).IsEqualTo(native.ExpiresAt);
        await Assert.That(call.Original.Observation.Kind).IsEqualTo(kind == GrainReadKind.WaitForAnnIndex
            ? AnnProjectionObservationKind.Wait : AnnProjectionObservationKind.Approximate);
        await RemoteAnnPublicationRf3Assertions.EqualAsync(state.Original, call.Original.Observation.OriginalRequest);
        await Assert.That(call.Definitions.IsDefaultOrEmpty).IsFalse();
        PartitionControlLifetimeCoverage.Require(call.Coverage, state.Scenario.Partition,
            call.Definitions, RemoteDocumentProtocol.MaximumBodyBytes);
        await Assert.That(call.Definitions.Select(value => value.Name).Distinct(StringComparer.Ordinal).Count())
            .IsEqualTo(call.Definitions.Length);
        await RemoteAnnPublicationRf3Assertions.EqualAsync(call.Definition,
            call.Definitions.Single(value => value.Name == NativeAnnMaintenanceRf3Scenario.Collection));
        await Assert.That(call.Definition.Name).IsEqualTo(NativeAnnMaintenanceRf3Scenario.Collection);
        byte[]? previous = null;
        foreach (var definition in call.Definitions)
        {
            JsonData.Identifier(definition.Name);
            await Assert.That(definition.TransactionDomainId).IsEqualTo(state.Scenario.Partition.TransactionDomainId);
            var key = KeySpace.Resource(state.Scenario.Partition.TenantId,
                state.Scenario.Partition.DatabaseId, definition.Name);
            if (previous is not null)
            {
                await Assert.That(previous.AsSpan().SequenceCompareTo(key)).IsLessThan(EqualKeyOrder);
            }
            previous = key;
        }
        await Assert.That(result.NodeId).IsEqualTo(state.Original.NodeId);
        await Assert.That(kind == GrainReadKind.WaitForAnnIndex ? result.Wait is not null && result.Approximate is null
            : result.Approximate is not null && result.Wait is null).IsTrue();
    }
}
