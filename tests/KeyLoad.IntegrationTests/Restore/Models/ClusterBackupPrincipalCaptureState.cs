using System.Collections.Immutable;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

internal sealed record ClusterBackupPrincipalCaptureState(PrincipalRecord Principal, string Credential,
    ClusterBackupOwnerRequest Request, ClusterBackupOwnerReceipt Original, ImmutableArray<string> Archives);
