using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PhysicalShardCatalogInterface34ObservationReader
{
    private const string InvalidEvidence = "The interface4 authenticated peer observation evidence is invalid.";

    internal static Task VerifyAsync(RequestCqrsProbeFixture fixture, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var owned = fixture.NodeFor(RequestCqrsRf3Protocol.Node1);
        RequestCqrsProbeFileStore.VerifyOwnerFile(owned.Directory, owned.OwnerBytes);
        var paths = RequestCqrsProbeFileValidation.ValidateContents(owned.Directory, allowDiscoveryRecords: true);
        var records = paths.Where(IsDiscoveryFile)
            .Select(path => (Path: path, Record: fixture.Json.ReadDiscovery(
                RequestCqrsProbeFileStore.ReadRecord(path))))
            .ToArray();
        if (records.Length != RequestCqrsProbeFixtureProtocol.MaximumDiscoveryRecords
            || records.Any(item => !ValidRecord(item.Path, item.Record, fixture.SessionId))
            || !records.Select(item => item.Record.PeerVoterId).OrderBy(peer => peer, StringComparer.Ordinal)
                .SequenceEqual(ExpectedPeers().OrderBy(peer => peer, StringComparer.Ordinal), StringComparer.Ordinal))
        { throw new InvalidOperationException(InvalidEvidence); }
        return Task.CompletedTask;
    }

    private static bool IsDiscoveryFile(string path)
        => Path.GetFileName(path) is RequestCqrsProbeFixtureProtocol.DiscoveryFile0
            or RequestCqrsProbeFixtureProtocol.DiscoveryFile1;

    private static bool ValidRecord(string path, RequestCqrsProbeDiscoveryRecord record, string sessionId)
    {
        var expectedPeer = Path.GetFileName(path) == RequestCqrsProbeFixtureProtocol.DiscoveryFile0
            ? RequestCqrsProbeFixtureProtocol.Node2Origin : RequestCqrsProbeFixtureProtocol.Node3Origin;
        return record.Version == RequestCqrsProbeFixtureProtocol.Version
            && record.Kind == RequestCqrsProbeFixtureProtocol.DiscoveryKind
            && record.SessionId == sessionId
            && record.ObserverVoterId == RequestCqrsProbeFixtureProtocol.Node1Origin
            && record.PeerVoterId == expectedPeer
            && record.ApplicationRpcVersion == 3 && record.PeerEnvelopeVersion == 3
            && !record.ProtocolCompatible;
    }

    private static IReadOnlyList<string> ExpectedPeers()
        => [RequestCqrsProbeFixtureProtocol.Node2Origin, RequestCqrsProbeFixtureProtocol.Node3Origin];
}
