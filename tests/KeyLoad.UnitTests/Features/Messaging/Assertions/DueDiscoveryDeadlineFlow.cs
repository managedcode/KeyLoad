using KeyLoad.Core;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class DueDiscoveryDeadlineFlow
{
    private const string IncompleteImage = "The bounded due-work fixture image has more canonical rows.";
    private const int InitialRevision = 1;
    private const int InitialGeneration = 1;
    private const int InitialOrdinal = 0;
    private const int AbsentRevision = 0;
    private const int SingleHint = 1;
    private const int FirstDueOffsetSeconds = -1;
    private static readonly Guid ScheduleId = DueWorkTestData.OrderedId(SingleHint);

    internal static async Task RunAsync()
    {
        var clock = new DueDiscoveryClock();
        using var fixture = new RecurringSagaDatabase(timeProvider: clock);
        var definition = DueWorkTestData.Definition(fixture.Queue, ScheduleId,
            RecurringSagaDatabase.Epoch.AddSeconds(FirstDueOffsetSeconds));
        var commandId = Guid.NewGuid();
        var request = new CommandRequest(commandId, fixture.Partition,
            [new ConfigureRecurringSchedule(definition, AbsentRevision)]);
        var receipt = fixture.Apply(OperationKind.Batch, request, id: commandId).Get<CommitReceipt>();
        var receiptBytes = NativeSerialization.Serialize(receipt);
        var image = Image(fixture);
        var identity = NativeSerialization.Serialize(fixture.Store.Identity);
        var position = fixture.Store.Position;
        var native = (ZoneTreeStore)fixture.Store;
        var baseline = native.GetReadDiagnostics().RangeExaminedBytes;
        clock.Arm(() => native.GetReadDiagnostics().RangeExaminedBytes > baseline,
            fixture.Database.DueDiscoveryDeadline);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
            DueWorkDiscovery.ReadPage(fixture.Database, null, DueWorkTestData.WakeAt));
        await Assert.That(clock.ExpiredAfterNativeProgress).IsTrue();
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(failure.Message).IsEqualTo(DueWorkProtocol.DeadlineExceeded);
        clock.Disarm();
        await UnchangedAsync(fixture, image, identity, position);
        var page = DueWorkDiscovery.ReadPage(fixture.Database, null, DueWorkTestData.WakeAt);
        var expected = new DueWorkHint(DueWorkKind.Schedule, fixture.Queue, ScheduleId,
            RecurringSagaDatabase.RootPrincipal, InitialRevision, InitialGeneration, InitialOrdinal, definition.FirstDueAt);
        await Assert.That(page.Jobs.Length).IsEqualTo(SingleHint);
        await Assert.That(NativeSerialization.Serialize(page.Jobs[InitialOrdinal])
            .SequenceEqual(NativeSerialization.Serialize(expected))).IsTrue();
        await Assert.That(page.Rejected.IsEmpty).IsTrue();
        await UnchangedAsync(fixture, image, identity, position);
        fixture.Reopen();
        var replay = fixture.Apply(OperationKind.Batch, request, id: commandId).Get<CommitReceipt>();
        await Assert.That(NativeSerialization.Serialize(replay).SequenceEqual(receiptBytes)).IsTrue();
        await UnchangedAsync(fixture, image, identity, position);
        var cold = DueWorkDiscovery.ReadPage(fixture.Database, null, DueWorkTestData.WakeAt);
        await Assert.That(NativeSerialization.Serialize(cold.Jobs)
            .SequenceEqual(NativeSerialization.Serialize(page.Jobs))).IsTrue();
        await Assert.That(cold.Rejected.IsEmpty).IsTrue();
        await UnchangedAsync(fixture, image, identity, position);
    }

    private static async Task UnchangedAsync(RecurringSagaDatabase fixture, string[] image,
        byte[] identity, long position)
    {
        await Assert.That(Image(fixture).SequenceEqual(image)).IsTrue();
        await Assert.That(fixture.Store.Position).IsEqualTo(position);
        await Assert.That(NativeSerialization.Serialize(fixture.Store.Identity).SequenceEqual(identity)).IsTrue();
    }

    private static string[] Image(RecurringSagaDatabase fixture) => fixture.Store.Read(view =>
    {
        var page = view.Scan([], fixture.Database.Limits.MaxResults);
        if (page.HasMore)
        {
            throw new InvalidOperationException(IncompleteImage);
        }
        return page.Records.Select(row => Convert.ToHexString(row.Key.Span) + ":" +
            Convert.ToHexString(row.Value.Span)).ToArray();
    });
}
