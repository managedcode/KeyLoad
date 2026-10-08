using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class UnconfiguredMovementGlobalCommandAssertions
{
    private const string Root = "root";
    private const string Subject = "movement-rejected-subject";
    private const string Tenant = "tenant";
    private const string Database = "database";
    private const string Collection = "authority-cost-documents";
    private const long PolicyEpoch = 1;

    internal static async Task RejectAsync(DatabaseEngine engine, TestDatabase database)
    {
        var principal = new PrincipalRecord(Subject, Tenant,
            [new(Database, Collection, Capability.DocumentsRead)], [])
        { PolicyEpoch = PolicyEpoch };
        var operation = engine.CreateNativeOperation(OperationKind.ConfigurePrincipal, Guid.NewGuid(), Root,
            engine.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(new ConfigurePrincipalRequest(principal)));
        var rejected = engine.Apply(operation);
        await Assert.That(rejected.Error).IsEqualTo(ErrorCode.OwnershipLost);
        await Assert.That(rejected.SafeDetail).IsEqualTo(PartitionMoveProtocol.Fenced);
        await Assert.That(rejected.Json).IsNull();
        await Assert.That(rejected.NativeValue).IsNull();
        var stored = database.Store.Read(view => view.GetRecord<PrincipalRecord>(KeySpace.Principal(Subject)));
        await Assert.That(stored).IsNull();
        var bytes = NativeSerialization.Serialize(rejected);
        var replay = engine.Apply(operation);
        await Assert.That(NativeSerialization.Serialize(replay).SequenceEqual(bytes)).IsTrue();
    }
}
