using KeyLoad.Query.Features.Search;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.Search;

internal static class TextCapturedPublicFlow
{
    internal static async Task RunAsync(bool cancelOriginal, CancellationToken token)
    {
        using var fixture = new TestDatabase();
        var seed = await TextOnlineSeedFixture.CreateAsync(fixture, token);
        await using var runtime = new TextOnlineTestRuntime(fixture);
        var request = new OnlineTextIndexMaintenanceRequest(Guid.NewGuid(), seed.Pin.Consumer,
            seed.Pin.Collection, seed.Pin.Field, seed.Pin.Generation, seed.Pin.NodeId, seed.Pin.Placement);
        var receipt = await TextOnlineWholeFlow.RunAsync(fixture, runtime, request, token);
        var files = TextCapturedFileRoster.Capture(fixture, runtime, request, token);
        await RunRetainedAsync(cancelOriginal, fixture, runtime, request, receipt, files, token);
        await runtime.DisposeAsync();
        await Assert.That(Directory.Exists(files.Path)).IsFalse();
        await using var cold = new TextOnlineTestRuntime(fixture);
        await TextCapturedPublicContinuation.HealthyAsync(cold, fixture, token);
        var coldReceipt = await TextOnlineWholeFlow.RunAsync(fixture, cold, request, token);
        await Assert.That(OrleansSerialization.Serialize(coldReceipt).SequenceEqual(OrleansSerialization.Serialize(receipt))).IsTrue();
        await TextCapturedPublicContinuation.HealthyAsync(cold, fixture, token);
    }

    private static async Task RunRetainedAsync(bool cancelOriginal, TestDatabase fixture,
        TextOnlineTestRuntime runtime, OnlineTextIndexMaintenanceRequest request,
        OnlineTextIndexMaintenanceResult receipt, TextCapturedFileRoster files, CancellationToken token)
    {
        using var originalCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var firstCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<RankedDocument[]>? original = null;
        var failures = new List<Exception>();
        var first = StartOriginal(runtime, fixture, firstEntered, firstRelease, firstCancellation.Token, token);
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await AwaitOriginalPostingAsync(firstEntered, first, token);
                original = StartOriginal(runtime, fixture, entered, release, originalCancellation.Token, token);
                await AwaitOriginalPostingAsync(entered, original, token);
                firstRelease.TrySetResult();
                await TextCapturedPublicContinuation.OriginalAsync(await first, fixture);
                files.RequireOriginal(fixture, runtime, token);
                var successor = await TextCapturedPublicContinuation.SwapAsync(fixture, runtime, request, token);
                files.RequireOriginal(fixture, runtime, token);
                var successorRequest = request with
                {
                    CommandId = successor.CommandId
                };
                var successorFiles = TextCapturedFileRoster.Capture(fixture, runtime, successorRequest, token);
                var retirement = runtime.Owner.CaptureOriginalRetirements().Single();
                if (cancelOriginal)
                {
                    await originalCancellation.CancelAsync();
                }
                release.TrySetResult();
                if (cancelOriginal)
                {
                    _ = await Assert.ThrowsAsync<OperationCanceledException>(async () => { _ = await original; });
                }
                else
                {
                    await TextCapturedPublicContinuation.OriginalAsync(await original, fixture);
                }
                await files.RequireRetirementAsync(retirement, successorFiles, fixture, runtime, successorRequest, token);
                await TextCapturedPublicContinuation.HealthyReplayAsync(fixture, runtime, request, receipt, successor, token);
                successorFiles.RequireCurrent(fixture, runtime, successorRequest, token);
            }, failures);
        }
        finally
        {
            await TextCapturedOriginalSettlement.JoinAsync(first, original, firstRelease, release,
                firstCancellation, originalCancellation, cancelOriginal, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private const string MissingOriginalPosting = "The original public caller completed without a retained execution posting observation.";

    internal static async Task AwaitOriginalPostingAsync(TaskCompletionSource entered,
        Task<RankedDocument[]> original, CancellationToken token)
    {
        _ = await Task.WhenAny(entered.Task, original).WaitAsync(token);
        if (!entered.Task.IsCompletedSuccessfully || original.IsCompleted)
        {
            _ = await original;
            throw new InvalidOperationException(MissingOriginalPosting);
        }
        await entered.Task;
        token.ThrowIfCancellationRequested();
    }

    internal static async Task CancelledCallerThenJoinedAsync(TextOnlineTestRuntime runtime,
        TestDatabase fixture, CancellationToken token)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var original = StartOriginal(runtime, fixture, entered, release, new CancellationToken(canceled: true), token);
        var failures = new List<Exception>();
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                _ = await Assert.ThrowsAsync<OperationCanceledException>(() => AwaitOriginalPostingAsync(entered, original, token));
            }, failures);
        }
        finally
        {
            release.TrySetResult();
            try
            {
                _ = await original;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception error) when (KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(error))
            {
                TextCapturedOriginalSettlement.RetainFailure(error, failures);
            }
            catch (Exception error) when (!KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(error))
            {
                TextCapturedOriginalSettlement.RetainFailure(error, failures);
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static Task<RankedDocument[]> StartOriginal(TextOnlineTestRuntime runtime, TestDatabase fixture,
        TaskCompletionSource entered, TaskCompletionSource release, CancellationToken caller, CancellationToken execution)
        => Task.Run(async () =>
        {
            using var observation = TextPostingObservation.Enter(async cancellation =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(cancellation);
            });
            return await runtime.Search.SearchAsync(TextMaintenanceTestValues.Principal,
                TextBilingualAudit.Request(fixture.Partition, "ПРИВІТ"), caller);
        }, execution);

}
