namespace KeyLoad.Comparisons.Targets;

internal sealed record RedisReplicaDiagnostic(string Predicate, string? PrimaryHost, int? PrimaryPort, string? ReplicaHost, int? ReplicaPort,
    string? InfoRole, string? InfoLink, string? InfoHost, int? InfoPort, int RoleCount, string? RoleName, string? RoleHost, int? RolePort, string? RoleState,
    bool ReplicationIdAgreement);
