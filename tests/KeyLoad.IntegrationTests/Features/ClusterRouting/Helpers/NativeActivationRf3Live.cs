using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Causal original callback evidence, not pending-task or process-alive inference.</summary>
internal static class NativeActivationRf3Live
{
    internal static async Task<RequestCqrsProbeLiveRecord> ChallengeAsync(RequestCqrsProbeFixture fixture,
        RequestCqrsProbeActivationRecord witness, int step, string predecessor, CancellationToken cancellationToken)
    {
        var node = RequestCqrsProbeFixtureProtocol.Nodes.Single(name => RequestCqrsProbeFileNames.OriginForNode(name) == witness.Voter);
        var owned = fixture.NodeFor(node);
        var challenge = new RequestCqrsProbeLiveRecord(witness.Version, RequestCqrsProbeLiveProtocol.ChallengeKind,
            witness.SessionId, witness.ArmId, witness.RequestId, witness.CommandId, witness.Voter, witness.SiloAddress,
            witness.GrainDigest, witness.ActivationId, step, Guid.NewGuid(), predecessor, RequestCqrsProbeLiveProtocol.EmptyDigest);
        var bytes = fixture.Json.WriteLive(challenge);
        RequestCqrsProbeFileStore.VerifyOwnerFile(owned.Directory, owned.OwnerBytes);
        RequestCqrsProbeFileStore.WriteAtomic(owned.Directory, RequestCqrsProbeLiveValidation.Name(challenge), bytes);
        var expected = challenge with
        {
            Kind = RequestCqrsProbeLiveProtocol.AckKind,
            ChallengeSha256 = RequestCqrsProbeLiveFiles.Hash(bytes)
        };
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestCqrsProbeFileStore.VerifyOwnerFile(owned.Directory, owned.OwnerBytes);
            var entries = RequestCqrsProbeFileValidation.ValidateContents(owned.Directory);
            RequireStillHeld(fixture, entries, witness);
            var path = entries.SingleOrDefault(path => Path.GetFileName(path) == RequestCqrsProbeLiveValidation.Name(expected));
            if (path is not null)
            {
                var ack = fixture.Json.ReadLive(RequestCqrsProbeFileStore.ReadRecord(path));
                await Assert.That(ack).IsEqualTo(expected);
                return ack;
            }
            await Task.Delay(NativeActivationRf3Protocol.ObservationInterval, TimeProvider.System, cancellationToken).ConfigureAwait(false);
        }
    }

    internal static void RequireStillHeld(RequestCqrsProbeFixture fixture, IEnumerable<string> paths,
        RequestCqrsProbeActivationRecord witness)
    {
        foreach (var path in paths.Where(path => Path.GetFileName(path).StartsWith(RequestCqrsProbeFixtureProtocol.MarkerFilePrefix, StringComparison.Ordinal)))
        {
            var marker = fixture.Json.ReadMarker(RequestCqrsProbeFileStore.ReadRecord(path));
            if (marker.ArmId != witness.ArmId || marker.RequestId != witness.RequestId)
            { continue; }
            if (marker.Outcome != RequestCqrsProbeOutcome.Observed)
            { throw new InvalidOperationException(NativeActivationRf3Protocol.Missing); }
        }
    }

    internal static string Digest(RequestCqrsProbeFixture fixture, RequestCqrsProbeLiveRecord ack)
        => ReadDigest(fixture, ack.Voter, RequestCqrsProbeLiveValidation.Name(ack));
    internal static string Digest(RequestCqrsProbeFixture fixture, RequestCqrsProbeActivationRecord witness)
        => ReadDigest(fixture, witness.Voter, RequestCqrsProbeActivationValidation.Name(witness));
    private static string ReadDigest(RequestCqrsProbeFixture fixture, string voter, string name)
    {
        var node = RequestCqrsProbeFixtureProtocol.Nodes.Single(node => RequestCqrsProbeFileNames.OriginForNode(node) == voter);
        var owned = fixture.NodeFor(node);
        RequestCqrsProbeFileStore.VerifyOwnerFile(owned.Directory, owned.OwnerBytes);
        var path = RequestCqrsProbeFileValidation.ValidateContents(owned.Directory).Single(path => Path.GetFileName(path) == name);
        return RequestCqrsProbeLiveFiles.Hash(RequestCqrsProbeFileStore.ReadRecord(path));
    }
}
