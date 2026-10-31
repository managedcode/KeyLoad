using KeyLoad.FunctionalTests.Authorization;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.FunctionalTests.DatabaseComposition;

internal sealed record CompositionLinkFailureImage(long Position, long Applied, byte[] Identity, string[] Rows)
{
    internal static CompositionLinkFailureImage Capture(SafeProjectorOperationDatabase owner,
        Command<DatabaseCommand> operation)
    {
        var key = KeySpace.PartitionOutcome(owner.Source.Partition, operation.UserId!, operation.CommandId);
        var locator = KeySpace.OutcomeLocator(owner.Source.Partition, operation.UserId!, operation.CommandId);
        if (owner.Store.Read(view => view.ReadOwnedValue(key)) is not null
            || owner.Store.Read(view => view.ReadOwnedValue(locator)) is not null)
        {
            throw new InvalidOperationException("The original failed composition command was already committed.");
        }
        return new(owner.Store.Position, owner.Database.LastApplied, OrleansSerialization.Serialize(owner.Store.Identity),
            Effects(owner, operation));
    }

    internal async Task RequireFailureAsync(SafeProjectorOperationDatabase owner,
        Command<DatabaseCommand> operation, string detail)
    {
        await Assert.That(Effects(owner, operation).SequenceEqual(Rows, StringComparer.Ordinal)).IsTrue();
        await Assert.That(OrleansSerialization.Serialize(owner.Store.Identity).AsSpan().SequenceEqual(Identity)).IsTrue();
        await Assert.That(owner.Store.Position).IsEqualTo(checked(Position + 1L));
        await Assert.That(owner.Database.LastApplied).IsEqualTo(checked(Applied + 1L));
        var actual = owner.ReadOutcome(operation);
        await Assert.That(actual.CanonicalSequence).IsEqualTo(checked(Applied + 1L));
        await Assert.That(actual.PreparedCommand.CommandId).IsEqualTo(operation.CommandId);
        await Assert.That(OrleansSerialization.Serialize(actual.PreparedCommand).AsSpan()
            .SequenceEqual(OrleansSerialization.Serialize(operation))).IsTrue();
        await Assert.That(OrleansSerialization.Serialize(actual.Result).AsSpan().SequenceEqual(
            OrleansSerialization.Serialize(Result<object>.Fail(Errors.Problem(ErrorCode.Validation, detail))))).IsTrue();
        var key = KeySpace.PartitionOutcome(owner.Source.Partition, operation.UserId!, operation.CommandId);
        var locator = owner.Store.Read(view => view.ReadOwnedValue(
            KeySpace.OutcomeLocator(owner.Source.Partition, operation.UserId!, operation.CommandId)));
        await Assert.That(locator is not null && locator.AsSpan().SequenceEqual(key)).IsTrue();
        var lastCommand = owner.Store.Read(view => OrleansSerialization.Deserialize<byte[]>(
            view.ReadOwnedValue(KeySpace.LastCommandBytes)!));
        await Assert.That(lastCommand.AsSpan().SequenceEqual(key)).IsTrue();
        var clock = owner.Store.Read(view => OrleansSerialization.Deserialize<DateTimeOffset>(
            view.ReadOwnedValue(KeySpace.ClockBytes)!));
        await Assert.That(clock).IsEqualTo(new DateTimeOffset(operation.Timestamp));
    }

    private static string[] Effects(SafeProjectorOperationDatabase owner, Command<DatabaseCommand> operation)
    {
        var excluded = new[]
        {
            KeySpace.PartitionOutcome(owner.Source.Partition, operation.UserId!, operation.CommandId),
            KeySpace.OutcomeLocator(owner.Source.Partition, operation.UserId!, operation.CommandId),
            KeySpace.AppliedBytes,
            KeySpace.LastCommandBytes,
            KeySpace.ClockBytes
        }.Select(key => Convert.ToHexString(key) + ":").ToArray();
        return CanonicalStoreImage.Bytes(owner.Store)
            .Where(row => !excluded.Any(prefix => row.StartsWith(prefix, StringComparison.Ordinal))).ToArray();
    }
}
