namespace KeyLoad.CrashHost.Features.ResourceExecution;

internal static class D2ImportCrashProtocol
{
    internal const string Mode = "d2-active-import-recovery";
    internal const string InputFile = "d2-active-import-input.native";
    internal const string ResultFile = "d2-active-import-result.native";
    internal const string InputAlias = "keyload.tests.d2-active-import-input.v1";
    internal const string SourceDirectory = "source";
    internal const string TargetDirectory = "target";
    internal const string Principal = "root";
    internal const string Tenant = "system";
    internal const string Database = "d2-import";
    internal const string Domain = "source-domain";
    internal const string LocalDomain = "retained-domain";
    internal const string Collection = "import-documents";
    internal const string LocalCollection = "retained-documents";
    internal const string DocumentId = "row";
    internal const string FirstPartition = "image-a";
    internal const string SecondPartition = "image-b";
    internal const string LocalFirst = "retained-a";
    internal const string LocalSecond = "retained-b";
    internal const string SourceJson = "{\"image\":true}";
    internal const string FirstJson = "{\"write\":\"during-both-imports\"}";
    internal const string SecondJson = "{\"write\":\"after-first-import\"}";
    internal const string PutKind = "putDocument";
    internal const int Version = 1;
    internal const int Empty = 0;
    internal const int Single = 1;
    internal const int Pair = 2;
    internal const int KeyBytes = 32;
    internal const int FirstField = 0;
    internal const int SecondField = 1;
    internal const int ThirdField = 2;
    internal const int FourthField = 3;
    internal static PartitionRef Partition(string key) => new(Tenant, Database, Domain, key);
    internal static PartitionRef LocalPartition(string key) => new(Tenant, Database, LocalDomain, key);
}

[Orleans.GenerateSerializer, Orleans.Alias(D2ImportCrashProtocol.InputAlias)]
internal sealed record D2ImportCrashInput(
    [property: Orleans.Id(D2ImportCrashProtocol.FirstField)] PhysicalShardRecord Owner,
    [property: Orleans.Id(D2ImportCrashProtocol.SecondField)] ReplicatedOperation Original,
    [property: Orleans.Id(D2ImportCrashProtocol.ThirdField)] long BeforePosition,
    [property: Orleans.Id(D2ImportCrashProtocol.FourthField)] long BeforeIndex);
