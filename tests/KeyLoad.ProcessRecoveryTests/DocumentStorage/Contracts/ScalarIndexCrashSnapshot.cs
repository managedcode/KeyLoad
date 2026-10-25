namespace KeyLoad.ProcessRecoveryTests.DocumentStorage;

internal sealed record ScalarIndexCrashSnapshot(ScalarIndexDocumentState[] Documents, ScalarIndexMembership[] Memberships);
