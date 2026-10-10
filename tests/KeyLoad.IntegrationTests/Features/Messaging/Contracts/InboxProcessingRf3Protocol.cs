namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class InboxProcessingRf3Protocol
{
    internal const string Tenant = "inbox-public-";
    internal const string Database = "database";
    internal const string Domain = "processing";
    internal const string Input = "input";
    internal const string Output = "output";
    internal const string Collection = "results";
    internal const string Message = "work";
    internal const string Document = "result";
    internal const string Refused = "refused";
    internal const string Handler = "handler";
    internal const string Payload = "{\"processed\":true}";
    internal const string Cold = "inbox-processing-cold";
    internal const long Generation = 1;
    internal const long InitialRevision = 0;
    internal const long MissingRevision = 9;
    internal const long FirstRevision = 1;
    internal const long DemotedEpoch = 2;
    internal const long RestoredEpoch = 3;
    internal static readonly string[] Nodes = ["node1", "node2", "node3"];
}
