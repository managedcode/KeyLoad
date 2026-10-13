using System.Collections.Immutable;

namespace KeyLoad.IntegrationTests.Features.ClientApi;

internal static class McpEventFeedDiscoveryExpectations
{
    private static readonly ImmutableArray<string> RequestProperties = ["version", "commandId", "controlPartition", "mapId", "action", "scope", "start", "expectedRevision", "expectedCoverageGeneration", "cursor", "offerDigest", "limit", "originalFirstIssuancePublicCommandId"];
    private static readonly ImmutableArray<string> ResultProperties = ["version", "publicCommandId", "mapId", "revision", "coverageGeneration", "released", "retained", "cursor", "offerDigest", "pages", "receipt"];
    internal static ImmutableArray<McpToolExpectation> Entries { get; } =
    [
        new("keyload_tail_event_feed", true, true, false, McpExpectedBody.Object, false,
            [],
            ResultProperties,
            BodyPropertyFields: RequestProperties),
        new("keyload_open_event_feed", false, false, true, McpExpectedBody.Object, false,
            [],
            ResultProperties,
            BodyPropertyFields: RequestProperties),
        new("keyload_offer_event_feed", false, false, true, McpExpectedBody.Object, false,
            [],
            ResultProperties,
            BodyPropertyFields: RequestProperties),
        new("keyload_refresh_event_feed", false, false, true, McpExpectedBody.Object, false,
            [],
            ResultProperties,
            BodyPropertyFields: RequestProperties),
        new("keyload_acknowledge_event_feed", false, false, true, McpExpectedBody.Object, false,
            [],
            ResultProperties,
            BodyPropertyFields: RequestProperties),
        new("keyload_release_event_feed", false, false, true, McpExpectedBody.Object, false,
            [],
            ResultProperties,
            BodyPropertyFields: RequestProperties)
    ];
}
