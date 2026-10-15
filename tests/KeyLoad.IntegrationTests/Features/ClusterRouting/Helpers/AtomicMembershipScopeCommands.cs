namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class AtomicMembershipScopeCommands
{
    internal const string Left = "membership-left";
    internal const string Right = "membership-right";
    internal const string InitialJson = "{\"value\":1}";
    internal const string UpdatedJson = "{\"value\":2}";
    internal const string Kind = "putDocument";
    internal const string ConflictDetail = "The command ID was already used with different content.";

    internal static CommandRequest Create(PartitionRef partition, long previous, string json)
        => new(Guid.NewGuid(), partition,
            [new PutDocument(RequestCqrsRf3Protocol.AdminCollection, Left, json,
                ExpectedRevision: previous, ExplicitReplacement: previous != 0),
             new PutDocument(RequestCqrsRf3Protocol.AdminCollection, Right, json,
                ExpectedRevision: previous, ExplicitReplacement: previous != 0)]);
}
