namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class DropRuntimeProtocol
{
    internal const string ObserverAlreadyOwned = "The execution DROP codec has a different phase observer.";
    internal const string MissingHold = "The execution DROP completed before its selected command observation.";
    internal const string MissingReply = "The execution DROP operation has no expected original reply.";
    internal const string InvalidCommand = "The execution DROP command identifier is empty.";
    internal const string DuplicateHold = "The execution DROP command already has an owned hold.";
    internal const string IdentityChanged = "The selected DROP request identity changed before producer disposal.";
    internal const string ReplyFailure = "execution-drop-reply-publication-failure";
    internal const int NoFailures = 0;
    internal const int RuntimeNodes = 2;
    internal const int PersistedCopies = 2;
    internal const int LocalPersistedCopies = 1;
    internal const int ColdReopens = 2;
    internal const int FirstCold = 0;
    internal const long NextCommit = 1;
    internal const int TinyScanRecords = 32;
    internal const string Tenant = "system";
    internal const string Database = "execution-drop";
    internal const string Domain = "execution-drop-domain";
    internal const string Table = "_drop_rows";
    internal const string Row = "row-1";
    internal const string IdField = "id";
    internal const string ValueField = "value";
    internal const string Json = "{\"id\":\"row-1\",\"value\":\"original\"}";
    internal const string OtherTable = "_drop_control";
    internal const string OtherRow = "control-1";
    internal const string OtherJson = "{\"id\":\"control-1\",\"value\":\"unrelated\"}";
    internal const string PartitionKey = "partition-a";
    internal static readonly PartitionRef Partition = new(Tenant, Database, Domain, PartitionKey);
    internal static DropTableRequest Request => new(Tenant, Database, Table);
}
