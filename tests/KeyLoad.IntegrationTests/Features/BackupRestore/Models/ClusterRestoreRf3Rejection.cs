using System.Collections.Immutable;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

/// <summary>Fixture-local invocation inputs only; never persisted, serialized or a database role.</summary>
internal sealed record ClusterRestoreRf3Rejection(ErrorCode Expected, bool Published, string? Credential,
    string? Detail, string? Signer, ImmutableArray<ClusterRestoreOwnerMapping>? Mappings);
