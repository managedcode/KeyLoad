using System.Collections.Immutable;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

/// <summary>Independent strict public CLI output schema, without CLI implementation coupling.</summary>
internal sealed record ClusterRestoreRf3OperatorReceipt(Guid CaptureId,
    ImmutableArray<ClusterRestoreRf3OperatorNode> Nodes, TimeSpan ActualElapsed);
