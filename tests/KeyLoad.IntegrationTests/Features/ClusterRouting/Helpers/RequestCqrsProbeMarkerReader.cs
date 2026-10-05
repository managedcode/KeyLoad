using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;
using static KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsProbeFixtureProtocol;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Polls only bounded server-written markers and validates them against signed discovery.</summary>
internal static class RequestCqrsProbeMarkerReader
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    internal static async Task<RequestCqrsProbeMarkerRecord> WaitForMarkerAsync(RequestCqrsProbeFixture fixture,
        Guid armId, RequestCqrsProbePhase phase, RequestCqrsProbeOutcome outcome,
        IReadOnlyList<ReplicaSiloDiscovery> signedDiscovery, CancellationToken cancellationToken)
    {
        EnsureScenarioDeadline(cancellationToken);
        ValidateDiscovery(signedDiscovery);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var match = FindMarker(fixture, armId, phase, outcome, null, signedDiscovery);
            if (match is not null)
            { return match; }
            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    internal static async Task WaitForSettlementAsync(RequestCqrsProbeFixture fixture, Guid armId,
        IReadOnlyList<ReplicaSiloDiscovery> signedDiscovery, CancellationToken cancellationToken)
    {
        EnsureScenarioDeadline(cancellationToken);
        ValidateDiscovery(signedDiscovery);
        var arm = fixture.ArmFor(armId);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var match = FindMarker(fixture, armId, arm.Phase, RequestCqrsProbeOutcome.Released,
                RequestCqrsProbeOutcome.Cancelled, signedDiscovery);
            if (match is not null)
            { return; }
            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private static RequestCqrsProbeMarkerRecord? FindMarker(RequestCqrsProbeFixture fixture, Guid armId,
        RequestCqrsProbePhase phase, RequestCqrsProbeOutcome outcome, RequestCqrsProbeOutcome? alternate,
        IReadOnlyList<ReplicaSiloDiscovery> signedDiscovery)
    {
        RequestCqrsProbeMarkerRecord? match = null;
        foreach (var node in Nodes)
        {
            var owned = fixture.NodeFor(node);
            var directory = owned.Directory;
            RequestCqrsProbeFileStore.VerifyOwnerFile(directory, owned.OwnerBytes);
            var entries = RequestCqrsProbeFileValidation.ValidateContents(directory);
            foreach (var path in entries)
            {
                var primaryMatch = Matches(path, armId, phase, outcome);
                var alternateMatch = alternate is { } alternateOutcome
                    && Matches(path, armId, phase, alternateOutcome);
                var matchedOutcome = primaryMatch ? outcome : alternateMatch ? alternate : null;
                if (matchedOutcome is null)
                { continue; }
                if (match is not null)
                { throw new InvalidOperationException(RequestCqrsProbeFixtureProtocol.MarkerMismatch); }
                var marker = RequestCqrsProbeJson.ReadMarker(RequestCqrsProbeFileStore.ReadRecord(path));
                if (marker.ArmId != armId || marker.Phase != phase || marker.Outcome != matchedOutcome
                    || Path.GetFileName(path) != RequestCqrsProbeFileNames.Marker(marker))
                { throw new InvalidOperationException(RequestCqrsProbeFixtureProtocol.MarkerMismatch); }
                ValidateMarkerCount(fixture, node, armId, marker.RequestId);
                fixture.RecordMarker(marker, node, phase.ToString(), matchedOutcome!.Value.ToString(), signedDiscovery);
                match = marker;
            }
        }
        return match;
    }

    private static bool Matches(string path, Guid armId, RequestCqrsProbePhase phase,
        RequestCqrsProbeOutcome outcome)
    {
        var name = Path.GetFileName(path);
        return name.StartsWith(MarkerFilePrefix + armId.ToString("N") + "-", StringComparison.Ordinal)
            && name.EndsWith("-" + phase + "-" + outcome + ".json", StringComparison.Ordinal);
    }

    private static void ValidateMarkerCount(RequestCqrsProbeFixture fixture, string node, Guid armId, Guid requestId)
    {
        var prefix = MarkerFilePrefix + armId.ToString("N") + "-" + requestId.ToString("N") + "-";
        var count = 0;
        foreach (var path in RequestCqrsProbeFileValidation.ValidateContents(fixture.NodeFor(node).Directory))
        {
            if (Path.GetFileName(path).StartsWith(prefix, StringComparison.Ordinal)
                && ++count > RequestCqrsProbeFixtureProtocol.MaximumMarkersPerRequestPerVoter)
            { throw new InvalidOperationException(RequestCqrsProbeFixtureProtocol.MarkerLimitExceeded); }
        }
    }

    private static void EnsureScenarioDeadline(CancellationToken cancellationToken)
    {
        if (!cancellationToken.CanBeCanceled)
        { throw new InvalidOperationException(RequestCqrsProbeFixtureProtocol.MissingScenarioDeadline); }
    }

    private static void ValidateDiscovery(IReadOnlyList<ReplicaSiloDiscovery> discovery)
    {
        if (discovery.Count != RequestCqrsRf3Protocol.NodeCount
            || discovery.Any(item => !item.TransportReady || string.IsNullOrWhiteSpace(item.SiloAddress))
            || discovery.Select(item => item.VoterId).Distinct(StringComparer.Ordinal).Count() != discovery.Count
            || discovery.Select(item => item.SiloAddress).Distinct(StringComparer.Ordinal).Count() != discovery.Count
            || discovery.Select(item => item.ClusterId).Distinct(StringComparer.Ordinal).Count() != 1
            || discovery.Select(item => item.Incarnation).Distinct().Count() != 1
            || discovery.Any(item => item.VoterId != Node1Origin && item.VoterId != Node2Origin
                && item.VoterId != Node3Origin))
        { throw new InvalidOperationException(RequestCqrsProbeFixtureProtocol.MarkerMismatch); }
    }
}

