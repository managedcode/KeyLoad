namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class NativeDropRuntimeProtocol
{
    internal const string ObserverAlreadyOwned = "The native DROP codec has a different phase observer.";
    internal const string MissingHold = "The native DROP completed before its selected command observation.";
    internal const string MissingReply = "The native DROP operation has no expected original reply.";
    internal const string InvalidCommand = "The native DROP command identifier is empty.";
    internal const string DuplicateHold = "The native DROP command already has an owned hold.";
    internal const string IdentityChanged = "The selected DROP request identity changed before producer disposal.";
    internal const string ReplyFailure = "native-drop-reply-publication-failure";
    internal const int NoFailures = 0;
    internal const int NativeNodes = 2;
    internal const int PersistedCopies = 2;
    internal const int LocalPersistedCopies = 1;
    internal const int ColdReopens = 2;
    internal const int FirstCold = 0;
    internal const long NextCommit = 1;
    internal const int TinyScanRecords = 4;
    internal const string Tenant = "system";
    internal const string Database = "native-drop";
    internal const string Domain = "native-drop-domain";
    internal const string Table = "native_drop_rows";
    internal const string Row = "row-1";
    internal const string IdField = "id";
    internal const string ValueField = "value";
    internal const string Json = "{\"id\":\"row-1\",\"value\":\"original\"}";
    internal const string OtherTable = "native_drop_control";
    internal const string OtherRow = "control-1";
    internal const string OtherJson = "{\"id\":\"control-1\",\"value\":\"unrelated\"}";
    internal const string PartitionKey = "partition-a";
    internal static readonly PartitionRef Partition = new(Tenant, Database, Domain, PartitionKey);
    internal static DropTableRequest Request => new(Tenant, Database, Table);
}
