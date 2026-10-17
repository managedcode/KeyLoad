namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class TargetInboxRf3Protocol
{
    internal const string Tenant = "target-inbox-";
    internal const string Database = "database";
    internal const string Domain = "work";
    internal const string Input = "input";
    internal const string Target = "target";
    internal const string Output = "output";
    internal const string Collection = "results";
    internal const string Document = "effect";
    internal const string Changed = "changed";
    internal const string Message = "work";
    internal const string Handler = "handler";
    internal const string SourcePartition = "source";
    internal const string TargetPartition = "target";
    internal const string CommandPath = "/v1/inbox/commit";
    internal const string CommandHeader = "X-KeyLoad-Command-Id";
    internal const int NoFailures = 0;
    internal const string Tool = "keyload_inbox_commit";
    internal const string Sql = "CALL keyload_inbox_commit(@args)";
    internal const string Arguments = "args";
    internal const string Payload = "{\"processed\":true}";
    internal const string Cold = "target-inbox-cold";
    internal const long Generation = 1;
    internal const long EmptyRevision = 0;
    internal const long BadRevision = 9;
    internal const long FirstRevision = 1;
    internal const long ReceiptCapacity = 2;
    internal const long ByteCapacity = 65536;
    internal const long DemotedEpoch = 2;
    internal const long RestoredEpoch = 3;
    internal static readonly string[] Nodes = ["node1", "node2", "node3"];
}
