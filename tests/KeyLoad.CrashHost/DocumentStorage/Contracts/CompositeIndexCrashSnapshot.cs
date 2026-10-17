namespace KeyLoad.CrashHost.Features.DocumentStorage;

internal sealed record CompositeIndexCrashSnapshot(CompositeIndexDocumentState[] Documents, CompositeIndexMembership[] Memberships, CompositePhysicalIndexImage[] IndexImages);
