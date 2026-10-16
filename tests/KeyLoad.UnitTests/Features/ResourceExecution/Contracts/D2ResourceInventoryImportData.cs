using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class D2ResourceInventoryImportData
{
    internal const string Resource = "d2-import-unrelated";
    internal const string Domain = "d2-import-unrelated-domain";
    internal const string PartitionKey = "retained-target";
    internal const string DocumentId = "concurrent";
    internal const string Json = "{\"committed\":true}";
    internal const int Version = 1;
    internal const int Empty = 0;
    internal const int Single = 1;
    internal const int KeyBytes = 32;
    internal const int TargetSetupEntries = 3;
    internal const int CapturedSourceEntries = 19;
    internal const int AuthorizationAndAcknowledgement = 2;
    internal const int StageAndInstallPasses = 2;
    internal const int FinalAndConcurrentEntries = 2;
    internal const int InstalledPageAndWriteEntries = 2;
    internal const string PutDocumentKind = "putDocument";
    internal static PartitionRef Partition => new(ControlledPartitionMovementCorpus.Partition.TenantId,
        ControlledPartitionMovementCorpus.Partition.DatabaseId, Domain, PartitionKey);
}
