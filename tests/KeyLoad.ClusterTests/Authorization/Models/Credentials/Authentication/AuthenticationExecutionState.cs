using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.DocumentStorage;

namespace KeyLoad.ClusterTests.Authorization;

internal sealed record AuthenticationExecutionState(McpDocumentScenario Document, PrincipalRecord Reader,
    ApiKeyRecord Credential, string Secret, CommandRequest OriginalCommand, CommitReceipt OriginalReceipt)
{
    internal DocumentResult Expected => new(Document.StoredReference, McpCallerProtocol.FirstRevision,
        McpDocumentProtocol.InitialJson, false, []);
}
