namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class NativeDropRuntimeProtocol
{
    internal const string ObserverAlreadyOwned = "The native DROP codec already has an original phase observer.";
    internal const string MissingHold = "The original native DROP completed before its required committed phase observation.";
    internal const int NoFailures = 0;
    internal const int MetadataHeadroomDivisor = 2;
    internal const long FirstPage = 1;
    internal const int OwnerGroups = 2;
    internal const int ColdReopens = 2;
    internal const string Tenant = "system";
    internal const string Database = "native-drop-pages";
    internal const string Domain = "native-drop-domain";
    internal const string Table = "native_drop_rows";
    internal const string Row = "row-1";
    internal const string IdField = "id";
    internal const string ValueField = "value";
    internal const string Json = "{\"id\":\"row-1\",\"value\":\"original\"}";
    internal const string MissingReply = "The native DROP operation has no expected original reply.";
    internal const string PartitionKey = "partition-a";
    internal static readonly PartitionRef Partition = new(Tenant, Database, Domain, PartitionKey);
    internal const string InvalidCommand = "The native DROP phase command identifier is empty.";
    internal const string DuplicateHold = "The native DROP phase command already has an owned hold.";
    internal const string IdentityChanged = "The selected native DROP phase request identity changed before producer disposal.";
}
