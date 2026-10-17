namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Retains actual bounded read-only status outcomes without private request or credential content.</summary>
internal sealed record ReplicaIsolationAuthorityObservation(string Resource, NodeStatus? Status,
    string? ErrorCode, int? HttpStatus, string? SafeDetail);
