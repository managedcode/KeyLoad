namespace KeyLoad.CrashHost.Features.DocumentStorage;

internal sealed record ScalarIndexCrashSnapshot(ScalarIndexDocumentState[] Documents, ScalarIndexMembership[] Memberships);
