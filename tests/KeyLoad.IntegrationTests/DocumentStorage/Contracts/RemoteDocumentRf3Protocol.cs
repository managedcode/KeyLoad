namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

internal static class RemoteDocumentRf3Protocol
{
    internal const string Tenant = "remote-tenant";
    internal const string Database = "remote-database";
    internal const string Domain = "remote-domain";
    internal const string Bucket = "remote-bucket";
    internal const string Collection = "remote-documents";
    internal const string Document = "one";
    internal const string Title = "title";
    internal const string Secret = "secret";
    internal const string Classification = "private";
    internal const string OriginalJson = "{\"title\":\"destination\",\"secret\":\"remote-private-canary\"}";
    internal const string ProjectedJson = "{\"title\":\"destination\"}";
    internal const string DocumentsGet = "keyload_documents_get";
    internal const string DocumentsCommit = "keyload_documents_commit";
    internal const string RestartScenario = "remote-document-destination-restart";
    internal const string ReaderPrefix = "remote-reader-";
    internal const string KeyPrefix = "remote-key-";
    internal const string SecretSeparator = ".";
    internal const string GuidFormat = "N";
    internal const int SecretBytes = 32;
    internal const int PlacementVersion = 1;
    internal const long UnboundRevision = 0;
    internal const long FirstRevision = 1;
    internal const long GrantedPolicyEpoch = 2;
    internal static readonly PartitionRef Partition = new(Tenant, Database, Domain, Bucket);
    internal static readonly EntityRef Reference = new(Partition, Collection, Document);
}
