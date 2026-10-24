namespace KeyLoad.IntegrationTests.Search;

internal static class TextDistinctTermsPublicScenario
{
    internal const string First = "rank-a";
    internal const string Second = "rank-b";
    internal const string Plain = "rank-c";
    internal const string FirstJson = "{\"text\":\"common common common common\"}";
    internal const string SecondJson = "{\"text\":\"rare common\"}";
    internal const string PlainJson = "{\"text\":\"rare\"}";
    internal const string ChangedJson = "{\"text\":\"common rare common rare\"}";
    internal const string HealthyJson = "{\"text\":\"rare common\"}";
    internal sealed record Original(CommandRequest Command, CommitReceipt Receipt);

    internal static CommandRequest Seed(PartitionRef partition)
        => new(Guid.NewGuid(), partition,
        [new PutDocument(TextClusterScenario.Collection, First, FirstJson, 0L),
         new PutDocument(TextClusterScenario.Collection, Second, SecondJson, 0L),
         new PutDocument(TextClusterScenario.Collection, Plain, PlainJson, 0L)]);

    internal static CommandRequest Change(PartitionRef partition)
        => new(Guid.NewGuid(), partition, [new PatchDocument(TextClusterScenario.Collection, First,
            [new("/text", PatchKind.Set, "\"common rare common rare\"")], 1L)]);

    internal static CommandRequest Delete(PartitionRef partition)
        => new(Guid.NewGuid(), partition, [new DeleteDocument(TextClusterScenario.Collection, Second, 1L)]);

    internal static CommandRequest Repair(PartitionRef partition)
        => new(Guid.NewGuid(), partition, [new PutDocument(TextClusterScenario.Collection, First, HealthyJson, 2L)]);
}
