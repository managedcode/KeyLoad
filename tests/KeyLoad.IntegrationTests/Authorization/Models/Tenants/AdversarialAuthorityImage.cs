namespace KeyLoad.IntegrationTests.Features.Authorization;

internal sealed record AdversarialAuthorityImage(DocumentResult Owned, DocumentResult Foreign,
    OutboxStatus OwnedOutbox, OutboxStatus ForeignOutbox);
