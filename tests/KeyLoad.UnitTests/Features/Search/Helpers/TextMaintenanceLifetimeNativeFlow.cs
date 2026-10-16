using KeyLoad.Clustering;
using KeyLoad.Query;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.ClusterReplication;
using KeyLoad.UnitTests.Features.ClusterRouting;
using K = KeyLoad.UnitTests.Features.Search.TextMaintenanceLifetimeNativeProtocol;
using P = KeyLoad.UnitTests.Features.ClusterReplication.ReplicaBarrierDeadlineScenario;

namespace KeyLoad.UnitTests.Features.Search;

internal static class TextMaintenanceLifetimeNativeFlow
{
    internal static async Task RunAsync(CancellationToken token)
    {
        var fixture = new RequestCqrsCohortRuntimeFixture(barrierDeadline: new ReplicaBarrierDeadlineScenario());
        var session = new NativeLocalChildLifetimeSession();
        TextMaintenanceLifetimeNativeRuntime? runtime = null;
        var failures = new List<Exception>();
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                var state = await NativeLocalChildLifetimeSeed.CreateAsync(fixture, token);
                var seeded = await TextMaintenanceLifetimeNativeSeed.CreateAsync(state, token);
                var operation = session.Initialize(state, token);
                runtime = new(state);
                var complete = await FinishAsync(session, operation, state, runtime, seeded.Request);
                await Assert.That(complete.TrackedRecords).IsEqualTo(K.RowCount);
                await Assert.That(operation.Ledger.RetainedEntries).IsEqualTo(K.LifetimeCount);
                await Assert.That(operation.Parent.Dependencies.Single().ResourceName).IsEqualTo(seeded.Request.Collection);
                await Assert.That(operation.Ledger.RetainedEncodedBytes)
                    .IsEqualTo(NativeSerialization.Measure(operation.Parent.Dependencies.Single()));
                await LiteralAsync(state, runtime, seeded.Request, operation.Token);
                var replay = (await state.Leader.Journal.Consensus.SubmitAsync(seeded.Write, token)).Get<CommitReceipt>();
                await NativeLocalChildLifetimeAssertions.EqualAsync(seeded.Receipt, replay);
                foreach (var voter in NativeLocalChildLifetimeProtocol.Voters)
                {
                    await fixture.Node(voter).Journal.Consensus.ReadBarrierAsync(token);
                    await NativeLocalChildLifetimeAssertions.RowsAsync(fixture.Node(voter), token);
                }
                NativeRequestWorkSettlement.Rethrow(await operation.Parent.CancelAsync());
                await CancelledAsync(session, operation, state, runtime, seeded.Request);
                await TextMaintenanceLifetimeNativeConservation.HealthyAfterCancellationAsync(fixture, state, token);
            }, failures);
        }
        finally
        {
            if (runtime is not null)
            {
                await ServerFailureObserver.ObserveAsync(() => runtime.CloseAsync(failures), failures);
            }
            await ServerFailureObserver.ObserveAsync(() => NativeLocalChildLifetimeCleanup.JoinAsync(session, failures), failures);
            try
            {
                await fixture.DisposeAsync();
            }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task<TextMaintenanceCapabilityResult> FinishAsync(NativeLocalChildLifetimeSession session,
        NativeLocalChildLifetimeOperation operation, NativeLocalChildLifetimeSeeded state,
        TextMaintenanceLifetimeNativeRuntime runtime, TextIndexMaintenanceRequest request)
    {
        var begin = await TextMaintenanceLifetimeNativeCalls.ExecuteAsync(session, operation, state, runtime,
            request, TextMaintenanceCapabilityKind.Begin);
        var page = state.Leader.Journal.Database.ReadProjectionBatch(P.Root,
            new(request.Consumer, ThroughSequence: begin.ReplayUpperSequence));
        await Assert.That(page.HasMore).IsFalse();
        var intent = new CommitProjectionBatchRequest(Guid.NewGuid(), request.Consumer, page.Token, []);
        _ = await TextMaintenanceLifetimeNativeCalls.ExecuteAsync(session, operation, state, runtime,
            request, TextMaintenanceCapabilityKind.PreparePage, page, intent);
        _ = await TextMaintenanceLifetimeNativeCalls.ExecuteAsync(session, operation, state, runtime,
            request, TextMaintenanceCapabilityKind.ApplyIntent);
        var committed = (await TextMaintenanceLifetimeNativeSeed.SubmitAsync(state,
            OperationKind.CommitProjectionBatch, intent.CommandId, intent, operation.Token)).Get<ProjectionBatchResult>();
        await Assert.That(committed.Receipt.CommandId).IsEqualTo(intent.CommandId);
        await Assert.That(committed.Receipt.Mutations).IsEmpty();
        _ = await TextMaintenanceLifetimeNativeCalls.ExecuteAsync(session, operation, state, runtime,
            request, TextMaintenanceCapabilityKind.SettleCheckpoint, receipt: committed);
        var complete = await TextMaintenanceLifetimeNativeCalls.ExecuteAsync(session, operation, state, runtime,
            request, TextMaintenanceCapabilityKind.Verify);
        await Assert.That(complete.ThroughSequence).IsEqualTo(page.ThroughSequence);
        return complete;
    }

    private static async Task LiteralAsync(NativeLocalChildLifetimeSeeded state,
        TextMaintenanceLifetimeNativeRuntime runtime, TextIndexMaintenanceRequest request, CancellationToken token)
    {
        var search = new SearchEngine(state.Leader.Journal.Database, runtime.Options.Core.QueryExecution, runtime.Projection);
        var selected = new TextIndexSelectionV1(request.Consumer, request.IndexGeneration);
        foreach (var expected in new[]
        {
            (Query: NativeTextBilingualAudit.EnglishQuery, Id: NativeTextBilingualAudit.EnglishId, Json: NativeTextBilingualAudit.EnglishJson),
            (Query: K.UkrainianQuery, Id: NativeTextBilingualAudit.UkrainianId, Json: NativeTextBilingualAudit.UkrainianJson)
        })
        {
            var query = NativeTextBilingualAudit.Request(P.Partition, expected.Query) with
            {
                TextIndex = selected
            };
            var hit = await Assert.That(await search.SearchAsync(P.Root, query, token)).HasSingleItem();
            var document = new DocumentResult(new(P.Partition, request.Collection, expected.Id),
                K.Revision, expected.Json, false, []);
            await Assert.That(JsonDefaults.Serialize(hit.Document).SequenceEqual(JsonDefaults.Serialize(document))).IsTrue();
            await Assert.That(hit.Score).IsEqualTo(K.SingleBranchScore);
        }
    }

    private static async Task CancelledAsync(NativeLocalChildLifetimeSession session, NativeLocalChildLifetimeOperation operation,
        NativeLocalChildLifetimeSeeded state, TextMaintenanceLifetimeNativeRuntime runtime, TextIndexMaintenanceRequest request)
    {
        OperationCanceledException? observed = null;
        try
        {
            _ = await TextMaintenanceLifetimeNativeCalls.ExecuteAsync(session, operation, state, runtime,
                request, TextMaintenanceCapabilityKind.Verify);
        }
        catch (OperationCanceledException error)
        {
            observed = error;
        }
        await Assert.That(observed).IsNotNull();
        await Assert.That(operation.Parent.Cancellation.IsCancellationRequested).IsTrue();
    }
}
