using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal sealed record PostgresCompositionRf3Image(DocumentResult Left, DocumentResult Right,
    global::KeyLoad.GraphTraversal Graph, MessageInspection Source, MessageInspection? Target);

internal sealed record PostgresCompositionRf3Seed(McpPersistedIdentity Identity, PrincipalRecord Principal,
    CommandRequest Seed, CommitReceipt SeedReceipt, CommandRequest Denied, PostgresCompositionRf3Image Before);

internal sealed record PostgresCompositionRf3State(PostgresCompositionRf3Seed Seed, CommandRequest Forward,
    CommitReceipt ForwardReceipt, CommandRequest Reverse, CommitReceipt ReverseReceipt, PostgresCompositionRf3Image Image);
