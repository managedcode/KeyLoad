using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class RequiredProcessingPolicyRefusal
{
    private static string[] Image(ZoneTreeStore store, byte[] outcome, byte[] locator) => store.Read(view =>
    {
        var page = view.Scan([], 4096);
        if (page.HasMore)
        {
            throw new InvalidOperationException("The complete required-processing image exceeded its bound.");
        }
        return page.Records.Where(row => !row.Key.Span.SequenceEqual(outcome) && !row.Key.Span.SequenceEqual(locator)
            && !row.Key.Span.SequenceEqual(KeySpace.ClockBytes))
            .Select(row => Convert.ToHexString(row.Key.Span) + ":" + Convert.ToHexString(row.Value.Span)).ToArray();
    });
    internal static async Task<byte[]> FreshAsync(RequiredProcessingPolicyFixture fixture, Command<DatabaseCommand> operation,
        ErrorCode code, string detail, CancellationToken token)
    {
        var store = fixture.Harness.Store;
        var outcome = KeySpace.PartitionOutcome(fixture.Harness.Partition, operation.UserId!, operation.CommandId);
        var locator = KeySpace.OutcomeLocatorV2(fixture.Harness.Partition, operation.UserId!, operation.CommandId);
        await Assert.That(store.Read(view => view.ReadOwnedValue(outcome))).IsNull();
        await Assert.That(store.Read(view => view.ReadOwnedValue(locator))).IsNull();
        var before = Image(store, outcome, locator);
        var position = store.Position;
        var identity = NativeSerialization.Serialize(store.Identity);
        var clock = store.Read(view => NativeSerialization.Deserialize<DateTimeOffset>(view.ReadOwnedValue(KeySpace.ClockBytes)!));
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
            await RequiredProcessingPolicyAssertions.FailureAsync(fixture.Harness.Database.ApplyEmbedded(operation, token), code, detail), failures);
        await ServerFailureObserver.ObserveAsync(async () => await Assert.That(Image(store, outcome, locator).SequenceEqual(before)).IsTrue(), failures);
        await ServerFailureObserver.ObserveAsync(async () => await Assert.That(store.Position).IsEqualTo(position + 1L), failures);
        await ServerFailureObserver.ObserveAsync(async () => await Assert.That(NativeSerialization.Serialize(store.Identity).AsSpan().SequenceEqual(identity)).IsTrue(), failures);
        await ServerFailureObserver.ObserveAsync(async () => await Assert.That(store.Read(view => view.ReadOwnedValue(locator))!
            .AsSpan().SequenceEqual(outcome)).IsTrue(), failures);
        await ServerFailureObserver.ObserveAsync(async () => await Assert.That(store.Read(view => NativeSerialization.Deserialize<DateTimeOffset>(
            view.ReadOwnedValue(KeySpace.ClockBytes)!))).IsGreaterThanOrEqualTo(clock), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        var bytes = RequiredProcessingPolicyAssertions.Outcome(store, fixture.Harness.Partition, operation.UserId!, operation.CommandId);
        await ExistingAsync(fixture.Harness.Database, store, fixture.Harness.Partition, operation, bytes, code, detail, token);
        return bytes;
    }
    internal static Task ExistingAsync(DatabaseEngine database, ZoneTreeStore store, PartitionRef partition, Command<DatabaseCommand> operation,
        byte[] outcome, ErrorCode code, string detail, CancellationToken token)
        => RequiredProcessingPolicyAssertions.ObserveAsync(store, async () =>
        {
            await RequiredProcessingPolicyAssertions.FailureAsync(database.ApplyEmbedded(operation, token), code, detail);
            await Assert.That(RequiredProcessingPolicyAssertions.Outcome(store, partition, operation.UserId!, operation.CommandId)
                .AsSpan().SequenceEqual(outcome)).IsTrue();
        });
}
