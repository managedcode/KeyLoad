using KeyLoad.UnitTests.Features.ClusterRouting;
namespace KeyLoad.UnitTests.Features.RelationalStorage;

internal static class SqlTypedInsertRetiredResourcePolicy
{
    internal static async Task RunAsync(ControlledPartitionMovementNode source, ControlledPartitionMovementNode target,
        SqlTypedInsertRetiredState state, CancellationToken token)
    {
        var partition = ControlledPartitionMovementCorpus.Partition;
        var denied = SqlTypedInsertRetiredLiteral.Definition(partition) with
        {
            SchemaVersion = DeniedVersion,
            FieldPolicies = [new(SqlTypedInsertRetiredLiteral.TitlePath, SqlTypedInsertRetiredLiteral.Classification,
                SqlTypedInsertRetiredLiteral.ReadGrant, SqlTypedInsertRetiredLiteral.UseGrant, MissingWriteGrant)]
        };
        await ConfigureAsync(source, target, denied, InitialVersion, token);
        await SqlTypedInsertRetiredImage.ObserveAsync(source, target,
            () => SqlTypedInsertRetiredAssertions.FencedAsync(source, state, FieldDenied, token),
            () => state.Authority.RemainsControlOwnedAsync(source, target),
            () => state.FailedAuthority.RemainsControlOwnedAsync(source, target));
        var repaired = SqlTypedInsertRetiredLiteral.Definition(partition) with
        {
            SchemaVersion = RepairedVersion
        };
        await ConfigureAsync(source, target, repaired, DeniedVersion, token);
        await SqlTypedInsertRetiredImage.ObserveAsync(source, target, async () =>
        {
            await SqlTypedInsertRetiredAssertions.ReplayAsync(source, target, state, token);
            await SqlTypedInsertRetiredAssertions.RowsAsync(target, repaired: false, token);
        }, () => state.Authority.RemainsControlOwnedAsync(source, target),
            () => state.FailedAuthority.RemainsControlOwnedAsync(source, target));
    }
    private static async Task ConfigureAsync(ControlledPartitionMovementNode source, ControlledPartitionMovementNode target,
        ResourceDefinition expected, long version, CancellationToken token)
    {
        await SqlTypedInsertAssertions.SameAsync(expected, Configure(source, expected, version, token));
        await SqlTypedInsertAssertions.SameAsync(expected, Configure(target, expected, version, token));
    }
    private static ResourceDefinition Configure(ControlledPartitionMovementNode node, ResourceDefinition next, long version, CancellationToken token)
    {
        var partition = ControlledPartitionMovementCorpus.Partition;
        var payload = new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId, next) { ExpectedSchemaVersion = version };
        return node.Journal.Submit(node.Database.CreateNativeOperation(OperationKind.ConfigureResource, Guid.NewGuid(),
            PhysicalShardCatalogFixture.RootPrincipalId, node.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(payload)), token).Get<ResourceDefinition>();
    }
    private const long InitialVersion = 1;
    private const long DeniedVersion = 2;
    private const long RepairedVersion = 3;
    private const string MissingWriteGrant = "retained.denied";
    private const string FieldDenied = "The mutation requires a protected field-write grant.";
}
