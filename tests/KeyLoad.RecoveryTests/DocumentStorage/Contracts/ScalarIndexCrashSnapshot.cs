namespace KeyLoad.RecoveryTests.Features.DocumentStorage;

internal sealed record ScalarIndexCrashSnapshot(ScalarIndexDocumentState[] Documents, ScalarIndexMembership[] Memberships);
