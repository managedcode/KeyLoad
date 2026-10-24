namespace KeyLoad.UnitTests.Authorization;

internal static class AuthorizationTelemetryPolicyRepair
{
    private const long RepairedPolicyEpoch = 2;

    internal static (string[] Bytes, long Position) Capture(TestDatabase database)
        => (global::KeyLoad.UnitTests.Messaging.QueueWholeFlowStorage.Bytes(database.Store), database.Store.Position);

    internal static async Task UnchangedAsync(TestDatabase database, (string[] Bytes, long Position) original)
    {
        await Assert.That(global::KeyLoad.UnitTests.Messaging.QueueWholeFlowStorage.Bytes(database.Store))
            .IsEquivalentTo(original.Bytes, global::TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(database.Store.Position).IsEqualTo(original.Position);
    }

    internal static async Task<long> RepairAsync(TestDatabase database)
    {
        var principal = new global::KeyLoad.PrincipalRecord(AuthorizationTelemetryTestProtocol.Member,
            database.Partition.TenantId,
            [new(database.Partition.DatabaseId, AuthorizationTelemetryTestProtocol.Resource, global::KeyLoad.Capability.All)], [])
        {
            ClusterAdministrator = true,
            PolicyEpoch = RepairedPolicyEpoch
        };
        var commandId = Guid.NewGuid();
        var request = new global::KeyLoad.ConfigurePrincipalRequest(principal);
        var original = database.Submit(global::KeyLoad.OperationKind.ConfigurePrincipal, request, id: commandId);
        await Assert.That(global::KeyLoad.OrleansSerialization.Serialize(original.Get<global::KeyLoad.PrincipalRecord>())
            .AsSpan().SequenceEqual(global::KeyLoad.OrleansSerialization.Serialize(principal))).IsTrue();
        var image = Capture(database);
        var originalBytes = global::KeyLoad.OrleansSerialization.Serialize(original);
        var replay = database.Submit(global::KeyLoad.OperationKind.ConfigurePrincipal, request, id: commandId);
        await Assert.That(global::KeyLoad.OrleansSerialization.Serialize(replay).AsSpan().SequenceEqual(originalBytes)).IsTrue();
        await UnchangedAsync(database, image);
        return database.Database.LastApplied;
    }
}
