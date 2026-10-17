namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal sealed record PostgresCompositionRf3Image(DocumentResult Left, DocumentResult Right,
    global::KeyLoad.GraphTraversal Graph, MessageInspection Source, MessageInspection? Target);
