namespace KeyLoad.RecoveryTests.DocumentStorage;

internal sealed record ScalarIndexCrashSnapshot(ScalarIndexDocumentState[] Documents, ScalarIndexMembership[] Memberships);
