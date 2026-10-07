using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class CanonicalApplyNetworkMarkers
{
    private const string GuidFormat = "N";
    private const string Separator = "-";
    private const string OutboundFailure = "The canonical apply owner invoked an external native replica transport.";
    internal static void RequireNoOutbound(RequestCqrsProbeFixture controls, Guid arm)
    {
        foreach (var node in RequestCqrsProbeFixtureProtocol.Nodes)
        {
            var owned = controls.NodeFor(node);
            RequestCqrsProbeFileStore.VerifyOwnerFile(owned.Directory, owned.OwnerBytes);
            foreach (var path in RequestCqrsProbeFileValidation.ValidateContents(owned.Directory))
            {
                var name = Path.GetFileName(path);
                if (!name.StartsWith(RequestCqrsProbeFixtureProtocol.MarkerFilePrefix + arm.ToString(GuidFormat) + Separator, StringComparison.Ordinal))
                { continue; }
                var marker = controls.Json.ReadMarker(RequestCqrsProbeFileStore.ReadRecord(path));
                if (marker.ArmId != arm)
                { throw new InvalidOperationException(RequestCqrsProbeFixtureProtocol.MarkerMismatch); }
                if (marker.Phase == RequestCqrsProbePhase.CanonicalOutboundObserved)
                { throw new InvalidOperationException(OutboundFailure); }
            }
        }
    }
}
