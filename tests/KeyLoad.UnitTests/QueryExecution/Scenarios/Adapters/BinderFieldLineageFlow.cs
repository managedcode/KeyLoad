using KeyLoad.Query;
using KeyLoad.Server;
using static KeyLoad.UnitTests.QueryExecution.BinderFieldLineageProtocol;

namespace KeyLoad.UnitTests.QueryExecution;

internal static class BinderFieldLineageFlow
{
    internal static async Task RunAsync(CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new TestDatabase();
            await ServerFailureObserver.ObserveAsync(() => ExecuteAsync(fixture, token), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExecuteAsync(TestDatabase fixture, CancellationToken token)
    {
        fixture.Configure(Collection, ResourceKind.Collection,
            fields: [new(ArrayPolicy, Classification), new(DottedPolicy, Classification)]);
        var command = new CommandRequest(Guid.NewGuid(), fixture.Partition,
            [new PutDocument(Collection, DocumentId, BinderFieldLineageRequests.OriginalJson)]);
        var receipt = fixture.Submit(OperationKind.Batch, command, id: command.CommandId).Get<CommitReceipt>();
        await BinderFieldLineageAuthority.SeedReceiptAsync(fixture, command, receipt);
        foreach (var mode in new[] { None, ReadOnly, UseOnly, Both })
        {
            await BinderFieldLineageAuthority.ConfigureAsync(fixture, ReaderPrefix + mode, mode, InitialEpoch, false);
        }
        var engine = new QueryEngine(fixture.Database, UnitExecutionOptions.QueryExecution());
        await BinderFieldLineagePages.ControlsAsync(engine, fixture.Store, fixture.Partition, token);
        await BinderFieldLineageAuthority.ConfigureAsync(fixture, ChangingSubject, None, InitialEpoch, false);
        var epoch = InitialEpoch;
        foreach (var mode in new[] { None, ReadOnly, UseOnly, Both, None, Both })
        {
            await BinderFieldLineageAuthority.ConfigureAsync(fixture, ChangingSubject, mode, ++epoch, false);
            await BinderFieldLineagePages.ModeAsync(engine, fixture.Store, fixture.Partition,
                ChangingSubject, mode, token);
        }
        await BinderFieldLineageAuthority.ConfigureAsync(fixture, ChangingSubject, Both, ++epoch, true);
        await BinderFieldLineagePages.RevokedAsync(engine, fixture.Store, fixture.Partition, token);
        await BinderFieldLineageAuthority.ConfigureAsync(fixture, ChangingSubject, Both, ++epoch, false);
        await BinderFieldLineagePages.ModeAsync(engine, fixture.Store, fixture.Partition,
            ChangingSubject, Both, token);
        await BinderFieldLineageAuthority.ReplayAsync(fixture.Database, fixture.Store, command, receipt, token);
        await BinderFieldLineageCold.RunAsync(fixture, command, receipt, token);
    }
}
