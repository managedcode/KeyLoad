namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class InitialConservationLineageProtocol
{
    internal const string Root = "root";
    internal const string Credential = "root.unit-test-credential-32-characters";
    internal const string Collection = "initial-lineage-documents";
    internal const string Document = "original";
    internal const string OriginalJson = "{\"value\":1}";
    internal const string UpdatedJson = "{\"value\":2}";
    internal const string FirstArchive = "initial-lineage-archive";
    internal const string SecondArchive = "nested-lineage-archive";
    internal const string FirstTarget = "initial-lineage-target";
    internal const string SecondTarget = "nested-lineage-target";
    internal const string StageSuffix = ".slot-stage";
    internal const string PutKind = "putDocument";
    internal const int One = 1;
    internal const int Two = 2;
    internal const int Empty = 0;
    internal const int SigningBytes = 32;
    internal static readonly string[] Endpoints =
        ["http://127.0.0.1:18001/", "http://127.0.0.1:18002/", "http://127.0.0.1:18003/"];
}
