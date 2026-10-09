using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Writes only to the exact owned voter which emitted the real activation witness.</summary>
internal static class NativeActivationMigrationRf3Control
{
    internal static RequestCqrsProbeMigrationRecord Write(RequestCqrsProbeFixture fixture,
        RequestCqrsProbeActivationRecord witness, ReplicaSiloDiscovery target, bool foreign)
    {
        var request = new RequestCqrsProbeMigrationRecord(witness.Version, RequestCqrsProbeMigrationProtocol.RequestKind,
            witness.SessionId, witness.ArmId, witness.RequestId, witness.CommandId, witness.Voter, witness.SiloAddress,
            witness.GrainDigest, witness.ActivationId, foreign
                ? NativeActivationMigrationRf3Protocol.ForeignVoter : target.VoterId, target.SiloAddress);
        var json = new RequestCqrsProbeMigrationJson(fixture.Json, IntegrationRoutingOptions.ProbeExecution());
        var bytes = json.Write(request);
        var node = RequestCqrsProbeFixtureProtocol.Nodes.Single(node => RequestCqrsProbeFileNames.OriginForNode(node) == witness.Voter);
        var owned = fixture.NodeFor(node);
        RequestCqrsProbeFileStore.VerifyOwnerFile(owned.Directory, owned.OwnerBytes);
        RequestCqrsProbeFileStore.WriteAtomic(owned.Directory, RequestCqrsProbeMigrationValidation.Name(request), bytes);
        return request;
    }

    internal static async Task RequireRequestedAsync(RequestCqrsProbeFixture fixture,
        RequestCqrsProbeMigrationRecord request, bool expected, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var node = RequestCqrsProbeFixtureProtocol.Nodes.Single(node => RequestCqrsProbeFileNames.OriginForNode(node) == request.Voter);
        var owned = fixture.NodeFor(node);
        RequestCqrsProbeFileStore.VerifyOwnerFile(owned.Directory, owned.OwnerBytes);
        var value = request with { Kind = RequestCqrsProbeMigrationProtocol.RequestedKind };
        var paths = RequestCqrsProbeFileValidation.ValidateContents(owned.Directory);
        var path = paths.SingleOrDefault(path => Path.GetFileName(path) == RequestCqrsProbeMigrationValidation.Name(value));
        await Assert.That(path is not null).IsEqualTo(expected);
        if (path is not null)
        {
            var json = new RequestCqrsProbeMigrationJson(fixture.Json, IntegrationRoutingOptions.ProbeExecution());
            await Assert.That(json.Read(RequestCqrsProbeFileStore.ReadRecord(path))).IsEqualTo(value);
        }
    }
}
