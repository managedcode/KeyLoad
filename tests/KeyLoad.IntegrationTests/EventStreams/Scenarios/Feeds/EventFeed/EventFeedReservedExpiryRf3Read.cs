using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class EventFeedReservedExpiryRf3Read
{
    private const string Invalid = "The original reserved event-feed expiry observation is inconsistent.";

    internal static DateTimeOffset Read(RequestCqrsProbeFixture fixture, RequestCqrsProbeMarkerRecord marker)
    {
        if (marker.Phase != RequestCqrsProbePhase.EventVectorFirstDispatchReserved
            || marker.Outcome != RequestCqrsProbeOutcome.Observed)
        {
            throw new InvalidOperationException(Invalid);
        }
        var node = RequestCqrsProbeFixtureProtocol.Nodes.Single(candidate
            => fixture.Json.ReadOwner(fixture.NodeFor(candidate).OwnerBytes).Voter == marker.Voter);
        var owned = fixture.NodeFor(node);
        RequestCqrsProbeFileStore.VerifyOwnerFile(owned.Directory, owned.OwnerBytes);
        var expected = new RequestCqrsEventVectorExpiryRecord(RequestCqrsProbeFixtureProtocol.Version,
            marker.RequestId, marker.CommandId, default);
        var name = RequestCqrsEventVectorExpiryJson.Name(expected);
        var paths = RequestCqrsProbeFileValidation.ValidateContents(owned.Directory);
        var path = paths.Single(candidate => Path.GetFileName(candidate) == name);
        var bytes = RequestCqrsProbeFileStore.ReadRecord(path);
        var actual = RequestCqrsEventVectorExpiryJson.Read(fixture.Json, bytes);
        if (actual.RequestId != marker.RequestId || actual.CommandId != marker.CommandId)
        {
            throw new InvalidOperationException(Invalid);
        }
        return actual.OriginalExpiresAt;
    }
}
