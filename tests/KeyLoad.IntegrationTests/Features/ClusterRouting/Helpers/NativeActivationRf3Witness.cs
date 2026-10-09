using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class NativeActivationRf3Witness
{
    internal static async Task<RequestCqrsProbeActivationRecord> ReadAsync(RequestCqrsProbeFixture controls,
        RequestCqrsProbeMarkerRecord marker, IReadOnlyList<ReplicaSiloDiscovery> discovery, CancellationToken cancellationToken)
    {
        var native = discovery.Single(item => item.VoterId == marker.Voter);
        await Assert.That(native.SiloAddress).IsEqualTo(marker.SiloAddress);
        var node = RequestCqrsProbeFixtureProtocol.Nodes.Single(name => RequestCqrsProbeFileNames.OriginForNode(name) == marker.Voter);
        var owned = controls.NodeFor(node);
        var name = RequestCqrsProbeActivationValidation.Name(marker.ArmId, marker.RequestId);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestCqrsProbeFileStore.VerifyOwnerFile(owned.Directory, owned.OwnerBytes);
            var path = RequestCqrsProbeFileValidation.ValidateContents(owned.Directory)
                .SingleOrDefault(candidate => Path.GetFileName(candidate) == name);
            if (path is not null)
            {
                var bytes = RequestCqrsProbeFileStore.ReadRecord(path);
                var witness = controls.Json.ReadActivation(bytes);
                RequestCqrsProbeActivationValidation.RequireMarker(witness, [marker]);
                await Assert.That(RequestCqrsProbeActivationValidation.Name(witness)).IsEqualTo(name);
                return witness;
            }
            await Task.Delay(NativeActivationRf3Protocol.ObservationInterval, TimeProvider.System,
                cancellationToken).ConfigureAwait(false);
        }
    }

    internal static async Task RequireReplacementAsync(RequestCqrsProbeActivationRecord first,
        RequestCqrsProbeActivationRecord replacement)
    {
        await Assert.That(replacement.GrainDigest).IsEqualTo(first.GrainDigest);
        await Assert.That(replacement.ActivationId).IsNotEqualTo(first.ActivationId);
        await Assert.That(replacement.CommandId).IsEqualTo(first.CommandId);
        await Assert.That(replacement.SessionId).IsEqualTo(first.SessionId);
        await Assert.That(replacement.RequestId).IsNotEqualTo(first.RequestId);
        await Assert.That(replacement.ArmId).IsNotEqualTo(first.ArmId);
        await Assert.That(replacement.SiloAddress).IsNotEqualTo(first.SiloAddress);
    }
}
