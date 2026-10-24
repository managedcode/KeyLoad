using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.DocumentStorage;

namespace KeyLoad.IntegrationTests.Authorization;

internal sealed record AuthenticationExecutionState(McpDocumentScenario Document, PrincipalRecord Reader,
    ApiKeyRecord Credential, string Secret, CommandRequest OriginalCommand, CommitReceipt OriginalReceipt)
{
    internal DocumentResult Expected => new(Document.StoredReference, McpCallerProtocol.FirstRevision,
        McpDocumentProtocol.InitialJson, false, []);
}
