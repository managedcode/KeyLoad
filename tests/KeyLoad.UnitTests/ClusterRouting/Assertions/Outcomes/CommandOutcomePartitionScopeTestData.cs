using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class CommandOutcomePartitionScopeTestData
{
    internal const string Resource = "outcome-documents";
    internal const string OtherAtomicPartition = "other-partition";
    internal const string FirstDocument = "first";
    internal const string SecondDocument = "second";
    internal const string FirstJson = "{\"scope\":\"first\"}";
    internal const string SecondJson = "{\"scope\":\"second\"}";

    internal static Command<DatabaseCommand> Operation<T>(TestDatabase database, Guid id, OperationKind kind, T request)
        => new(id, kind, "root", database.Database.EvaluationClock.GetUtcNow(),
            JsonSerializer.Serialize(request, JsonDefaults.Options));

    internal static StoredOutcome ReadStored(TestDatabase database, Command<DatabaseCommand> operation)
        => OutcomeStoreOracle.ReadStored(database.Store, operation)!;

    internal static async Task AssertCorruptionWithoutMutationAsync(TestDatabase database,
        Command<DatabaseCommand> operation)
    {
        var scope = CommandOutcomePartitionIdentity.Resolve(database.Database.OwnCommand(operation));
        var partition = scope.Partition ?? throw new InvalidOperationException("The corruption oracle requires a partition operation.");
        var outcomeKey = OutcomeStoreOracle.Key(operation);
        var locatorKey = KeySpace.OutcomeLocatorV2(partition, operation.UserId!, operation.CommandId);
        var position = database.Store.Position;
        var outcome = database.Store.Read(view => view.ReadOwnedValue(outcomeKey));
        var locator = database.Store.Read(view => view.ReadOwnedValue(locatorKey));
        await Assert.That(Errors.Code(database.Database.ResolveOutcome(operation).Problem)).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(EqualBytes(database.Store.Read(view => view.ReadOwnedValue(outcomeKey)), outcome)).IsTrue();
        await Assert.That(EqualBytes(database.Store.Read(view => view.ReadOwnedValue(locatorKey)), locator)).IsTrue();
    }

    private static bool EqualBytes(byte[]? left, byte[]? right)
        => left is null ? right is null : right is not null && left.AsSpan().SequenceEqual(right);

    internal static byte[]? ReadLocator(TestDatabase database, PartitionRef partition, Guid commandId)
        => database.Store.Read(view => view.ReadOwnedValue(KeySpace.OutcomeLocatorV2(partition, "root", commandId)));
}
