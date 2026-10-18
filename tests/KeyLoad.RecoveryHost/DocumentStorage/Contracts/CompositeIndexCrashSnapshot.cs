namespace KeyLoad.RecoveryHost.Features.DocumentStorage;

internal sealed record CompositeIndexCrashSnapshot(CompositeIndexDocumentState[] Documents, CompositeIndexMembership[] Memberships, CompositePhysicalIndexImage[] IndexImages);
