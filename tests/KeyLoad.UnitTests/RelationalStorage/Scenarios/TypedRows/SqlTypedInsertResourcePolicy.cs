using KeyLoad.UnitTests.Messaging;
namespace KeyLoad.UnitTests.RelationalStorage;

internal static class SqlTypedInsertResourcePolicy
{
    internal static async Task RunAsync(TestDatabase fixture, SqlTypedInsertState state)
    {
        var original = SqlTypedInsertLiteral.Definition(fixture.Partition);
        var denied = original with
        {
            SchemaVersion = DeniedVersion,
            FieldPolicies = [new(SqlTypedInsertLiteral.TitlePath, SqlTypedInsertLiteral.Classification,
                SqlTypedInsertLiteral.ReadGrant, SqlTypedInsertLiteral.UseGrant, SqlTypedInsertLiteral.MissingGrant)]
        };
        await ConfigureAsync(fixture, denied, original.SchemaVersion);
        var image = QueueWholeFlowStorage.Bytes(fixture.Store);
        var cut = fixture.Store.Position;
        await Assert.That(Errors.Code(SqlTypedInsertLiteral.Apply(fixture.Database, state.Created.Command, state.Created.At).Problem))
            .IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(Errors.Code(SqlTypedInsertLiteral.Apply(fixture.Database, state.Failed.Command, state.Failed.At).Problem))
            .IsEqualTo(ErrorCode.PermissionDenied);
        await SqlTypedInsertAssertions.UnchangedAsync(fixture.Store, image, cut);
        await BytesAsync(fixture, state);
        await ConfigureAsync(fixture, original with
        {
            SchemaVersion = RepairedVersion
        }, denied.SchemaVersion);
        await SqlTypedInsertHot.ReplayAsync(fixture, state.Created);
        await SqlTypedInsertHot.ReplayAsync(fixture, state.Failed);
        await SqlTypedInsertAssertions.ImageAsync(fixture, fixture.Store, fixture.Database, repaired: false);
    }
    private static async Task ConfigureAsync(TestDatabase fixture, ResourceDefinition expected, long previous)
        => await SqlTypedInsertAssertions.SameAsync(expected, fixture.Submit(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(fixture.Partition.TenantId, fixture.Partition.DatabaseId, expected) { ExpectedSchemaVersion = previous }).Get<ResourceDefinition>());
    internal static async Task BytesAsync(TestDatabase fixture, SqlTypedInsertState state)
    {
        await SqlTypedInsertAssertions.SameAsync(state.Created.Bytes,
            SqlTypedInsertAssertions.OutcomeBytes(fixture.Store, fixture.Partition, state.Created.Command.CommandId));
        await SqlTypedInsertAssertions.SameAsync(state.Failed.Bytes,
            SqlTypedInsertAssertions.OutcomeBytes(fixture.Store, fixture.Partition, state.Failed.Command.CommandId));
    }
    private const long DeniedVersion = 2;
    private const long RepairedVersion = 3;
}
