namespace KeyLoad.Comparisons.Targets;

internal enum RedisReplicaFailedPredicate
{
    None,
    InfoRole,
    InfoLink,
    InfoHost,
    InfoPort,
    RoleCardinality,
    RoleName,
    HostAgreement,
    RolePort,
    PortAgreement,
    ConfiguredPrimary,
}
