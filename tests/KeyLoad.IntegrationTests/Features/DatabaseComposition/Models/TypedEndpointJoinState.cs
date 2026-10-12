using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal sealed record TypedEndpointJoinModels(DocumentResult First, DocumentResult Second,
    DocumentResult Hidden, DocumentResult? Missing, ImmutableArray<MessageInspection> Sources,
    global::KeyLoad.GraphTraversal Graph, global::KeyLoad.GraphTraversal HiddenGraph,
    global::KeyLoad.GraphTraversal? MissingGraph, MessageInspection? MalformedSource);

internal sealed record TypedEndpointJoinSeed(PartitionRef Partition, ResourceDefinition ToDefinition,
    McpPersistedIdentity Identity, PrincipalRecord Principal, TypedEndpointJoinModels Models);

internal sealed record TypedEndpointJoinState(TypedEndpointJoinSeed Seed, CommandRequest OriginalDenied,
    JsonElement OriginalProblem, CommandRequest Healthy, CommitReceipt Receipt,
    CommandRequest EmptyHealthy, CommitReceipt EmptyReceipt, TypedEndpointJoinModels Models,
    CommandRequest? Continuation = null, CommitReceipt? ContinuationReceipt = null,
    CommandRequest? Malformed = null, JsonElement? MalformedProblem = null);
