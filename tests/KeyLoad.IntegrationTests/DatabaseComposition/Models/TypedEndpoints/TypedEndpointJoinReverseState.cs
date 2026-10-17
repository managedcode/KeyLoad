using System.Text.Json;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal sealed record TypedEndpointJoinReverseState(global::KeyLoad.IntegrationTests.Features.ClientApi.McpPersistedIdentity Identity, PrincipalRecord Principal,
    CommandRequest Denied, JsonElement OriginalProblem, JsonElement DeniedProblem,
    CommandRequest Command, CommitReceipt Receipt, JsonElement? FencedProblem = null,
    CommandRequest? Current = null, CommitReceipt? CurrentReceipt = null);
