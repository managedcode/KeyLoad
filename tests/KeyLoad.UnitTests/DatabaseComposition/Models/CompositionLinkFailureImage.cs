using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Storage;
using KeyLoad.UnitTests.Features.Authorization;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Features.DatabaseComposition;

internal sealed record CompositionLinkFailureImage(long Position, long Applied, byte[] Identity, string[] Rows)
{
    internal static CompositionLinkFailureImage Capture(SafeProjectorOperationDatabase owner,
        Command<DatabaseCommand> operation)
    {
        var key = KeySpace.PartitionOutcome(owner.Source.Partition, operation.UserId!, operation.CommandId);
        var locator = KeySpace.OutcomeLocatorV2(owner.Source.Partition, operation.UserId!, operation.CommandId);
        if (owner.Store.Read(view => view.ReadOwnedValue(key)) is not null
            || owner.Store.Read(view => view.ReadOwnedValue(locator)) is not null)
        {
            throw new InvalidOperationException("The original failed composition command was already committed.");
        }
        return new(owner.Store.Position, owner.Database.LastApplied, NativeSerialization.Serialize(owner.Store.Identity),
            Effects(owner, operation));
    }

    internal async Task RequireFailureAsync(SafeProjectorOperationDatabase owner,
        Command<DatabaseCommand> operation, string detail)
    {
        await Assert.That(Effects(owner, operation).SequenceEqual(Rows, StringComparer.Ordinal)).IsTrue();
        await Assert.That(NativeSerialization.Serialize(owner.Store.Identity).AsSpan().SequenceEqual(Identity)).IsTrue();
        await Assert.That(owner.Store.Position).IsEqualTo(checked(Position + 1L));
        await Assert.That(owner.Database.LastApplied).IsEqualTo(checked(Applied + 1L));
        var actual = owner.ReadOutcome(operation);
        await Assert.That(actual.CanonicalSequence).IsEqualTo(checked(Applied + 1L));
        await Assert.That(actual.Fingerprint).IsEqualTo(DatabaseCommandFingerprint.Compute(operation));
        await Assert.That(NativeSerialization.Serialize(actual.PreparedCommand).AsSpan()
            .SequenceEqual(NativeSerialization.Serialize(operation))).IsTrue();
        await Assert.That(NativeSerialization.Serialize(actual.Result).AsSpan().SequenceEqual(
            NativeSerialization.Serialize(Result<object>.Fail(Errors.Problem(ErrorCode.Validation, detail))))).IsTrue();
        var key = KeySpace.PartitionOutcome(owner.Source.Partition, operation.UserId!, operation.CommandId);
        var locator = owner.Store.Read(view => view.ReadOwnedValue(
            KeySpace.OutcomeLocatorV2(owner.Source.Partition, operation.UserId!, operation.CommandId)));
        await Assert.That(locator is not null && locator.AsSpan().SequenceEqual(key)).IsTrue();
        var clock = owner.Store.Read(view => NativeSerialization.Deserialize<DateTimeOffset>(
            view.ReadOwnedValue(KeySpace.ClockBytes)!));
        await Assert.That(clock).IsEqualTo(new DateTimeOffset(operation.Timestamp));
    }

    private static string[] Effects(SafeProjectorOperationDatabase owner, Command<DatabaseCommand> operation)
    {
        var excluded = new[]
        {
            KeySpace.PartitionOutcome(owner.Source.Partition, operation.UserId!, operation.CommandId),
            KeySpace.OutcomeLocatorV2(owner.Source.Partition, operation.UserId!, operation.CommandId),
            KeySpace.AppliedBytes,
            KeySpace.ClockBytes
        }.Select(key => Convert.ToHexString(key) + ":").ToArray();
        return CanonicalStoreImage.Bytes(owner.Store)
            .Where(row => !excluded.Any(prefix => row.StartsWith(prefix, StringComparison.Ordinal))).ToArray();
    }
}
