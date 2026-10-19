namespace KeyLoad.UnitTests.Features.Search;

internal static class TextWaitObservationPermission
{
    internal const string Reader = "execution-text-observation-reader";
    internal const string Field = "/text";

    internal static PrincipalRecord Configure(TestDatabase database, bool allowField)
        => database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(
            new(Reader, database.Partition.TenantId,
                [new(database.Partition.DatabaseId, TextBilingualAudit.Collection,
                    Capability.DocumentsRead | Capability.Query)],
                allowField ? ["pii.read", "pii.use"] : [])
            {
                PolicyEpoch = allowField ? 2L : 1L
            })).Get<PrincipalRecord>();
}
