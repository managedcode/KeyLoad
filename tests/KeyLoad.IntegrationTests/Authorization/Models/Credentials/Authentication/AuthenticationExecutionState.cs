using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.DocumentStorage;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal sealed record AuthenticationExecutionState(McpDocumentScenario Document, PrincipalRecord Reader,
    ApiKeyRecord Credential, string Secret, CommandRequest OriginalCommand, CommitReceipt OriginalReceipt)
{
    internal DocumentResult Expected => new(Document.StoredReference, McpCallerProtocol.FirstRevision,
        McpDocumentProtocol.InitialJson, false, []);
}
