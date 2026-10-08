using KeyLoad.Query;
using KeyLoad.UnitTests.Features.Messaging;
using KeyLoad.UnitTests.Features.QueryExecution;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class NativeTextWaitObservedWorkTests
{
    private const int DeadlineSeconds = 1;
    private const int ExpiredSeconds = 2;
    private const long InitialSchemaVersion = 1;
    private const long InitialPolicyEpoch = 1;
    private const string Root = "root";
    private const string TextField = "/text";
    private const string DeadlineDetail = "The read execution deadline is exceeded.";

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ActualNativePublicationWorkCancellationOrDeadlineSettlesWithoutPartialThenLiteralHealthy(bool cancel)
    {
        var clock = new QueryObservedWorkClock();
        await using var fixture = new ReplicaAppliedPositionWaitFixture(
            new DatabaseLimits { QueryDeadlineSeconds = DeadlineSeconds }, timeProvider: clock);
        var database = fixture.Canonical;
        database.Configure(NativeTextBilingualAudit.Collection, ResourceKind.Collection);
        var receipt = await NativeTextWaitSeed.CommitAsync(fixture, TestContext.Current!.Execution.CancellationToken);
        using var projection = NativeTextBilingualAudit.Open(database);
        var search = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection);
        var request = new WaitForIndexRequest(database.Partition, NativeTextBilingualAudit.Collection, TextField, receipt.Token);
        var image = QueueWholeFlowStorage.Bytes(database.Store);
        var cut = database.Store.Position;
        var before = database.Store.GetReadDiagnostics();
        using var cancellation = new CancellationTokenSource();
        clock.Arm(() => database.Store.GetReadDiagnostics().RangeExaminedBytes > before.RangeExaminedBytes,
            () => { if (cancel) { cancellation.Cancel(); } else { clock.Advance(TimeSpan.FromSeconds(ExpiredSeconds)); } });
        WaitForIndexResult? partial = null;
        try
        {
            if (cancel)
            {
                var failure = await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
                    { partial = await search.WaitForIndexAsync(Root, request, cancellation.Token); }) ?? throw new InvalidOperationException();
                await Assert.That(failure.CancellationToken).IsEqualTo(cancellation.Token);
            }
            else
            {
                var failure = await Assert.ThrowsExactlyAsync<KeyLoadException>(async () =>
                    { partial = await search.WaitForIndexAsync(Root, request, cancellation.Token); }) ?? throw new InvalidOperationException();
                await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
                await Assert.That(failure.Message).IsEqualTo(DeadlineDetail);
            }
        }
        finally { clock.Disarm(); }
        await Assert.That(clock.Triggered).IsTrue();
        await Assert.That(partial).IsNull();
        await Assert.That(database.Store.Position).IsEqualTo(cut);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(image, CollectionOrdering.Matching);
        var healthy = await search.WaitForIndexAsync(Root, request, TestContext.Current!.Execution.CancellationToken);
        await Assert.That(NativeSerialization.Serialize(healthy.AppliedToken).SequenceEqual(NativeSerialization.Serialize(receipt.Token))).IsTrue();
        await Assert.That(healthy.SchemaVersion).IsEqualTo(InitialSchemaVersion);
        await Assert.That(healthy.PolicyEpoch).IsEqualTo(InitialPolicyEpoch);
        await NativeTextBilingualAudit.VerifyAsync(search, database.Partition, TestContext.Current!.Execution.CancellationToken);
        await Assert.That(database.Store.Position).IsEqualTo(cut);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(image, CollectionOrdering.Matching);
    }
}
