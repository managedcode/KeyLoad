using KeyLoad.Query;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication.Commands;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class QueryAdapterNullParityPolicy
{
    internal static async Task RequireAsync(DatabaseEngine database, ZoneTreeStore store, PartitionRef partition,
        int epoch, List<(Command<DatabaseCommand> Operation, byte[] Result)> originals)
    {
        await SetAsync(database, partition, epoch, false, originals);
        var engine = new QueryEngine(database, UnitExecutionOptions.QueryExecution());
        foreach (var route in new[] { 0, 1, 2 })
        {
            await QueryAdapterNullParityConservation.ObserveAsync(store, async () =>
            {
                var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
                {
                    _ = route == 0 ? engine.Execute(QueryAdapterNullParityProtocol.Reader,
                        new(partition, QueryAdapterNullParityProtocol.Sql, AllowFullScan: true))
                        : engine.ExecuteAst(QueryAdapterNullParityProtocol.Reader, route == 1
                            ? QueryAdapterNullParityProtocol.Json(partition) : QueryAdapterNullParityProtocol.CSharp(partition));
                });
                await Assert.That(failure.Code).IsEqualTo(ErrorCode.PermissionDenied);
                await Assert.That(failure.Message).IsEqualTo("The principal cannot perform this operation in this scope.");
            });
        }
        await SetAsync(database, partition, epoch + 1, true, originals);
        await QueryAdapterNullParityPages.HealthyAsync(engine, store, partition, QueryAdapterNullParityProtocol.Reader);
    }
    private static async Task SetAsync(DatabaseEngine database, PartitionRef partition, int epoch, bool allow,
        List<(Command<DatabaseCommand> Operation, byte[] Result)> originals)
    {
        var expected = new PrincipalRecord(QueryAdapterNullParityProtocol.Reader, partition.TenantId,
            allow ? [new(partition.DatabaseId, QueryAdapterNullParityProtocol.Collection,
                Capability.Query | Capability.DocumentsRead)] : [], [])
        {
            PolicyEpoch = epoch
        };
        var bytes = new byte[16];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes, epoch);
        bytes[15] = 51;
        var actual = QueryAdapterNullParityReplay.Apply(database, OperationKind.ConfigurePrincipal,
            new Guid(bytes), new ConfigurePrincipalRequest(expected), originals).Get<PrincipalRecord>();
        await Assert.That(JsonDefaults.Serialize(actual)).IsEquivalentTo(JsonDefaults.Serialize(expected), CollectionOrdering.Matching);
    }
}
