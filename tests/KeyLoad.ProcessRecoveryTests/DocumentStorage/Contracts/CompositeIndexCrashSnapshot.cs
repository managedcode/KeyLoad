namespace KeyLoad.ProcessRecoveryTests.DocumentStorage;

internal sealed record CompositeIndexCrashSnapshot(CompositeIndexDocumentState[] Documents, CompositeIndexMembership[] Memberships, CompositePhysicalIndexImage[] IndexImages);
