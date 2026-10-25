using System.Text.Json;

namespace KeyLoad.ClusterTests.DatabaseComposition;

internal sealed record TypedEndpointJoinReverseState(global::KeyLoad.ClusterTests.ClientApi.McpPersistedIdentity Identity, PrincipalRecord Principal,
    CommandRequest Denied, JsonElement OriginalProblem, JsonElement DeniedProblem,
    CommandRequest Command, CommitReceipt Receipt, JsonElement? FencedProblem = null,
    CommandRequest? Current = null, CommitReceipt? CurrentReceipt = null);
