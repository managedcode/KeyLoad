using System.Text.Json;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal sealed record TypedEndpointJoinState(TypedEndpointJoinSeed Seed, CommandRequest OriginalDenied,
    JsonElement OriginalProblem, CommandRequest Healthy, CommitReceipt Receipt,
    CommandRequest EmptyHealthy, CommitReceipt EmptyReceipt, TypedEndpointJoinModels Models,
    CommandRequest? Continuation = null, CommitReceipt? ContinuationReceipt = null,
    CommandRequest? Malformed = null, JsonElement? MalformedProblem = null,
    TypedEndpointJoinReverseState? Reverse = null);
