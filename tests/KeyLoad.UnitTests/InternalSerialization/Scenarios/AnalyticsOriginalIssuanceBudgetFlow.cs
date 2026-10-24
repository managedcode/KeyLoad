using KeyLoad.Core.InternalSerialization;
using KeyLoad.UnitTests.Authorization;
using KeyLoad.UnitTests.GraphTraversal;
using ManagedCode.Communication.Commands;
using A = KeyLoad.UnitTests.InternalSerialization.AnalyticsCommandIdentityAssertions;
using D = KeyLoad.UnitTests.InternalSerialization.AnalyticsCommandIdentityData;
using O = KeyLoad.UnitTests.AnalyticsStore.AnalyticsStoreTestOperations;

namespace KeyLoad.UnitTests.InternalSerialization;

internal static class AnalyticsOriginalIssuanceBudgetFlow
{
    internal static Task RunAsync(AnalyticsIdentityBudgetFault fault, CancellationToken token)
        => SafeProjectorOperationDatabase.RunAsync(async owner =>
        {
            var definition = new ResourceDefinition(D.Store, ResourceKind.AnalyticsStore, owner.Source.Partition.TransactionDomainId)
            {
                AnalyticsSchema = new([D.First, D.Last], [D.First, D.Last])
            };
            await O.EqualAsync(definition, owner.Source.Submit(OperationKind.ConfigureResource,
                new ConfigureResourceRequest(owner.Source.Partition.TenantId, owner.Source.Partition.DatabaseId, definition))
                .Get<ResourceDefinition>());
            owner.Source.Configure(D.Collection, ResourceKind.Collection);
            var reference = A.Reference(owner);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            var clock = new GraphTraversalDeadlineClock();
            var work = new ReadExecutionBudget(owner.Database.OperationLimitsOptions,
                fault == AnalyticsIdentityBudgetFault.Expired ? clock : owner.Database.EvaluationClock, cancellation.Token);
            work.ConstrainWork(work.RuntimeMaximumReadBytes, work.MaximumScanRecords);
            var principal = owner.Store.Read(view => work.ReadRecord<PrincipalRecord>(view, KeySpace.Principal(D.Root)));
            await Assert.That(principal!.Id).IsEqualTo(D.Root);
            var spentBytes = work.ReadBytes;
            var spentRecords = work.ConstrainedRecords;
            await Assert.That(spentBytes).IsGreaterThan(0);
            await Assert.That(spentRecords).IsGreaterThan(0);
            Constrain(fault, owner, work, clock, cancellation);
            await A.UnchangedAsync(owner, async () =>
            {
                var request = D.Request(owner.Source, Guid.NewGuid(), false, false);
                await RefuseAsync(owner, request, work, fault, cancellation.Token);
                if (fault is AnalyticsIdentityBudgetFault.SpentBytes or AnalyticsIdentityBudgetFault.SpentRecords
                    or AnalyticsIdentityBudgetFault.Cancelled or AnalyticsIdentityBudgetFault.Expired)
                {
                    await Assert.That(work.ReadBytes).IsEqualTo(spentBytes);
                    await Assert.That(work.ConstrainedRecords).IsEqualTo(spentRecords);
                }
            });
            if (fault == AnalyticsIdentityBudgetFault.Expired)
            {
                await Assert.That(clock.ElapsedCapTriggered).IsTrue();
            }

            token.ThrowIfCancellationRequested();
            await HealthyAsync(owner, reference, token);
        }, token);

    private static void Constrain(AnalyticsIdentityBudgetFault fault, SafeProjectorOperationDatabase owner,
        ReadExecutionBudget work, GraphTraversalDeadlineClock clock, CancellationTokenSource cancellation)
    {
        switch (fault)
        {
            case AnalyticsIdentityBudgetFault.SpentBytes:
                work.ConstrainWork(work.ReadBytes, work.MaximumScanRecords);
                break;
            case AnalyticsIdentityBudgetFault.SpentRecords:
                work.ConstrainWork(work.RuntimeMaximumReadBytes, work.ConstrainedRecords);
                break;
            case AnalyticsIdentityBudgetFault.Cancelled:
                cancellation.Cancel();
                break;
            case AnalyticsIdentityBudgetFault.Expired:
                clock.ArmAfterWork(() => work.ReadBytes > 0,
                    TimeSpan.FromSeconds(owner.Database.OperationLimitsOptions.Value.QueryDeadlineSeconds).Add(TimeSpan.FromTicks(1)));
                break;
        }
    }

    private static async Task RefuseAsync(SafeProjectorOperationDatabase owner, CommandRequest request,
        ReadExecutionBudget work, AnalyticsIdentityBudgetFault fault, CancellationToken token)
    {
        Command<DatabaseCommand>? issued = null;
        Action issue = () => issued = owner.Database.CreateCommand(OperationKind.Batch, request.CommandId, D.Root,
            owner.Database.EvaluationClock.GetUtcNow(), OrleansSerialization.Serialize(request), work);
        if (fault == AnalyticsIdentityBudgetFault.IdentityOutput)
        {
            issue = () => AnalyticsIdentity.Canonicalize(request, work, 1);
        }

        if (fault == AnalyticsIdentityBudgetFault.MissingOriginalWork)
        {
            issue = () => issued = owner.Database.CreateCommand(OperationKind.Batch, request.CommandId, D.Root,
                owner.Database.EvaluationClock.GetUtcNow(), OrleansSerialization.Serialize(request));
        }

        if (fault == AnalyticsIdentityBudgetFault.Cancelled)
        {
            var cancelled = Assert.ThrowsExactly<OperationCanceledException>(() => issue());
            await Assert.That(cancelled.CancellationToken).IsEqualTo(token);
        }
        else
        {
            var failure = Assert.ThrowsExactly<KeyLoadException>(() => issue());
            await Assert.That(failure.Code).IsEqualTo(fault == AnalyticsIdentityBudgetFault.MissingOriginalWork
                ? ErrorCode.UnsupportedCapability : ErrorCode.BudgetExceeded);
        }
        await Assert.That(issued).IsNull();
    }

    private static async Task HealthyAsync(SafeProjectorOperationDatabase owner, EntityRef reference, CancellationToken token)
    {
        var request = D.Request(owner.Source, Guid.NewGuid(), false, true);
        var work = new ReadExecutionBudget(owner.Database.OperationLimitsOptions, owner.Database.EvaluationClock, token);
        var issued = owner.Database.CreateCommand(OperationKind.Batch, request.CommandId, D.Root,
            owner.Database.EvaluationClock.GetUtcNow(), OrleansSerialization.Serialize(request), work);
        await A.OrderAsync(issued, false);
        var expected = new CommitReceipt(request.CommandId, owner.NextToken(), [new("putDocument", D.Collection, D.Document, 1), new(AnalyticsStoreProtocol.UpsertKind, D.Store, D.Last, 1)], DurabilityProfile.ProcessDurable, 1);
        await A.ReceiptAsync(expected, owner.Submit(issued).Get<CommitReceipt>());
        await A.RowsAsync(owner, false, reference, token);
    }
}
