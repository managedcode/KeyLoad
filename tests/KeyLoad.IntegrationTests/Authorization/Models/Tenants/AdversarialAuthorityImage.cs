namespace KeyLoad.IntegrationTests.Authorization;

internal sealed record AdversarialAuthorityImage(DocumentResult Owned, DocumentResult Foreign,
    OutboxStatus OwnedOutbox, OutboxStatus ForeignOutbox);
