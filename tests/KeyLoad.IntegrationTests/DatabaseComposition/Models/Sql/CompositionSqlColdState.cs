using KeyLoad.IntegrationTests.Features.RelationalStorage;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal sealed record CompositionSqlColdState(RelationalSqlClusterScenario Scenario, QueueGraphLink Link,
    CommandRequest Forward, CommitReceipt ForwardReceipt, CommandRequest Reverse, CommitReceipt ReverseReceipt,
    DocumentResult First, DocumentResult Second, global::KeyLoad.GraphTraversal Graph,
    MessageInspection Source, MessageInspection Derived);
