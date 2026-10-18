namespace KeyLoad.RecoveryHost.Features.DocumentStorage;

internal sealed record ScalarIndexCrashSnapshot(ScalarIndexDocumentState[] Documents, ScalarIndexMembership[] Memberships);
