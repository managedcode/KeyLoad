using KeyLoad.Query;
using KeyLoad.Server;
using static KeyLoad.UnitTests.Features.QueryExecution.QueryEquivalentIntegerDecimalProtocol;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class QueryEquivalentIntegerDecimalFlow
{
    internal static async Task RunAsync(CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new TestDatabase();
            await ServerFailureObserver.ObserveAsync(() => BodyAsync(fixture, token), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task BodyAsync(TestDatabase fixture, CancellationToken token)
    {
        fixture.Configure(Collection, ResourceKind.Collection);
        var seed = new CommandRequest(Guid.NewGuid(), fixture.Partition,
            [.. Records.Select(row => (Mutation)new PutDocument(Collection, row.Id, row.Json))]);
        var receipt = fixture.Submit(OperationKind.Batch, seed, id: seed.CommandId).Get<CommitReceipt>();
        await QueryEquivalentIntegerDecimalCurrent.ReceiptAsync(fixture, seed, receipt);
        var engine = new QueryEngine(fixture.Database, UnitExecutionOptions.QueryExecution());
        await QueryEquivalentIntegerDecimalCurrent.PolicyAsync(fixture, InitialEpoch, true, false);
        await QueryEquivalentIntegerDecimalPages.RunAsync(engine, fixture.Store, fixture.Partition, null, token);
        await QueryEquivalentIntegerDecimalCurrent.PolicyAsync(fixture, DeniedEpoch, false, false);
        await QueryEquivalentIntegerDecimalPages.RunAsync(engine, fixture.Store, fixture.Partition, ErrorCode.PermissionDenied, token);
        await QueryEquivalentIntegerDecimalCurrent.PolicyAsync(fixture, RepairEpoch, true, false);
        await QueryEquivalentIntegerDecimalPages.RunAsync(engine, fixture.Store, fixture.Partition, null, token);
        await QueryEquivalentIntegerDecimalCurrent.PolicyAsync(fixture, RevokedEpoch, true, true);
        await QueryEquivalentIntegerDecimalPages.RunAsync(engine, fixture.Store, fixture.Partition, ErrorCode.Unauthenticated, token);
        await QueryEquivalentIntegerDecimalCurrent.PolicyAsync(fixture, FinalEpoch, true, false);
        await QueryEquivalentIntegerDecimalPages.RunAsync(engine, fixture.Store, fixture.Partition, null, token);
        await QueryEquivalentIntegerDecimalCurrent.ReplayAsync(fixture.Database, fixture.Store, seed, receipt, token);
        await QueryEquivalentIntegerDecimalCold.RunAsync(fixture, seed, receipt, token);
    }
}
