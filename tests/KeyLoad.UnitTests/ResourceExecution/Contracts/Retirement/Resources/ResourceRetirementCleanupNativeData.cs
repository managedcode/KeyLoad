using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class ResourceRetirementCleanupNativeData
{
    internal const string Table = "cleanup-table";
    internal const string Other = "cleanup-other";
    internal const string Id = "id";
    internal const string Name = "name";
    internal const string Index = "by-name";
    internal const string NamePath = "/name";
    internal const string First = "row-a";
    internal const string Second = "row-b";
    internal const string FirstJson = "{\"id\":\"row-a\",\"name\":\"alpha\"}";
    internal const string SecondJson = "{\"id\":\"row-b\",\"name\":\"beta\"}";
    internal const string OtherPartition = "cleanup-second";
    internal const string UnselectedPartition = "cleanup-unselected";
    internal const string Retained = "retained-native-leaf-sentinel";
    internal const string Field = "field";
    internal const string Derived = "derived";
    internal const string Tokenizer = "tokenizer";
    internal const string Principal = "root";
    internal const string Group = "group";
    internal const string DocumentFamily = "document";
    internal const string IndexFamily = "index";
    internal const string UniqueFamily = "unique";
    internal const string DocumentEpochFamily = "document-epoch";
    internal const string VisibilityEpochFamily = "visibility-epoch";
    internal const string VectorFamily = "vector";
    internal const string LineageFamily = "vector-projection-lineage";
    internal const string Bm25Family = "bm25-corpus-current";
    internal const string TextFamily = "online-text-current";
    internal const string EdgeFamily = "edge";
    internal const string EventFamily = "event";
    internal const string HistorySpace = "retained-cleanup-history";
    internal const string CheckpointSpace = "unit-cleanup-checkpoint";
    internal const string Rollback = "Deliberate atomic cleanup rollback.";
    internal const string PageBound = "The finite native cleanup did not complete.";
    internal const int MutationLimit = 2;
    internal const int RowsPerPartition = 2;
    internal const int HexCharactersPerByte = 2;
    internal const int CursorKeyCopies = 2;
    internal const int ReservedMutations = 1;
    internal const int FamilyCount = 9;
    internal const int ImageLimit = 1_024;
    internal const int MaximumPages = 128;
    internal const int FirstIndex = 0;
    internal const int SecondIndex = 1;
    internal const int One = 1;
    internal const int Zero = 0;
    internal const long Generation = 1;
    internal const long ReservedBytes = 4_096;
    internal static string[] Families => [DocumentFamily, IndexFamily, UniqueFamily, DocumentEpochFamily, VisibilityEpochFamily,
        VectorFamily, LineageFamily, Bm25Family, TextFamily];

    internal static byte[] Checkpoint(PartitionRef partition)
        => KeyCodec.Encode(CheckpointSpace, partition.TenantId, partition.DatabaseId,
            partition.TransactionDomainId, partition.PartitionKey);
}
