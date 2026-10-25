namespace KeyLoad.ClusterTests.Search;

internal static class TextNormalizationPublicScenario
{
    internal const string First = "normal-a";
    internal const string Second = "normal-b";
    internal const string Plain = "normal-c";
    internal const string FirstJson = "{\"text\":\"café needle\"}";
    internal const string SecondJson = "{\"text\":\"ＣＡＦÉ needle\"}";
    internal const string PlainJson = "{\"text\":\"cafe needle\"}";
    internal const string ChangedJson = "{\"text\":\"plain\"}";
    internal const string HealthyJson = "{\"text\":\"café fresh\"}";
    internal sealed record Original(CommandRequest Command, CommitReceipt Receipt);

    internal static CommandRequest Seed(PartitionRef partition)
        => new(Guid.NewGuid(), partition,
        [new PutDocument(TextClusterScenario.Collection, First, FirstJson, 0L),
         new PutDocument(TextClusterScenario.Collection, Second, SecondJson, 0L),
         new PutDocument(TextClusterScenario.Collection, Plain, PlainJson, 0L)]);

    internal static CommandRequest Change(PartitionRef partition)
        => new(Guid.NewGuid(), partition, [new PatchDocument(TextClusterScenario.Collection, First,
            [new("/text", PatchKind.Set, "\"plain\"")], 1L)]);

    internal static CommandRequest Delete(PartitionRef partition)
        => new(Guid.NewGuid(), partition, [new DeleteDocument(TextClusterScenario.Collection, Second, 1L)]);

    internal static CommandRequest Repair(PartitionRef partition)
        => new(Guid.NewGuid(), partition, [new PutDocument(TextClusterScenario.Collection, First, HealthyJson, 2L)]);
}
