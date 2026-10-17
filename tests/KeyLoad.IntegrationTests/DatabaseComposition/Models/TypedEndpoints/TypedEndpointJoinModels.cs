using System.Collections.Immutable;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal sealed record TypedEndpointJoinModels(DocumentResult First, DocumentResult Second,
    DocumentResult Hidden, DocumentResult? Missing, ImmutableArray<MessageInspection> Sources,
    global::KeyLoad.GraphTraversal Graph, global::KeyLoad.GraphTraversal HiddenGraph,
    global::KeyLoad.GraphTraversal? MissingGraph, MessageInspection? MalformedSource);
