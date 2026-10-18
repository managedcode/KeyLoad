using ManagedCode.Communication.Commands;
using ManagedCode.Communication;
using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class QueryAdapterNullParityReplay
{
    internal static async Task RequireAsync(DatabaseEngine database, ZoneTreeStore store,
        List<(Command<DatabaseCommand> Operation, byte[] Result)> originals)
    {
        foreach (var original in originals)
        {
            await QueryAdapterNullParityConservation.ObserveAsync(store, async () =>
            {
                var replay = database.ApplyEmbedded(original.Operation, cancellationToken: default);
                await Assert.That(JsonDefaults.Serialize(replay)).IsEquivalentTo(original.Result, CollectionOrdering.Matching);
            });
        }
    }
    internal static Result<object> Apply<T>(DatabaseEngine database, OperationKind kind, Guid id, T payload,
        List<(Command<DatabaseCommand> Operation, byte[] Result)> originals)
    {
        var operation = database.CreateCommand(kind, id, QueryAdapterNullParityProtocol.Root, default, System.Text.Json.JsonSerializer.Serialize(payload, JsonDefaults.Options));
        var result = database.ApplyEmbedded(operation, cancellationToken: default);
        originals.Add((operation, JsonDefaults.Serialize(result)));
        return result;
    }
}
