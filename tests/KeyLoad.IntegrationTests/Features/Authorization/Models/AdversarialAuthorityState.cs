using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.DocumentStorage;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal sealed record AdversarialAuthoritySeed(McpDocumentScenario Scenario, CommandRequest Command,
    CommitReceipt Receipt);
internal sealed record AdversarialAuthorityImage(DocumentResult Owned, DocumentResult Foreign,
    OutboxStatus OwnedOutbox, OutboxStatus ForeignOutbox);
internal sealed record AdversarialAuthorityState(AdversarialAuthoritySeed Owned,
    AdversarialAuthoritySeed Foreign, McpPersistedIdentity Identity);
