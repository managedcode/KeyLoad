using KeyLoad.Query.Features.Search;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.Search;

internal static class NativeTextCapturedPublicFlow
{
    internal static async Task RunAsync(bool cancelOriginal, CancellationToken token)
    {
        using var fixture = new TestDatabase(nativeReplicaAdmission: true);
        var seed = await NativeTextOnlineSeedFixture.CreateAsync(fixture, token);
        await using var runtime = new NativeTextOnlineTestRuntime(fixture);
        var request = new OnlineTextIndexMaintenanceRequest(Guid.NewGuid(), seed.Pin.Consumer,
            seed.Pin.Collection, seed.Pin.Field, seed.Pin.Generation, seed.Pin.NodeId, seed.Pin.Placement);
        var receipt = await NativeTextOnlineWholeFlow.RunAsync(fixture, runtime, request, token);
        var files = NativeTextCapturedFileRoster.Capture(fixture, runtime, request, token);
        await RunRetainedAsync(cancelOriginal, fixture, runtime, request, receipt, files, token);
        await runtime.DisposeAsync();
        await Assert.That(Directory.Exists(files.Path)).IsFalse();
        await using var cold = new NativeTextOnlineTestRuntime(fixture);
        await NativeTextCapturedPublicContinuation.HealthyAsync(cold, fixture.Partition, token);
        var coldReceipt = await NativeTextOnlineWholeFlow.RunAsync(fixture, cold, request, token);
        await Assert.That(NativeSerialization.Serialize(coldReceipt).SequenceEqual(NativeSerialization.Serialize(receipt))).IsTrue();
        await NativeTextCapturedPublicContinuation.HealthyAsync(cold, fixture.Partition, token);
    }

    private static async Task RunRetainedAsync(bool cancelOriginal, TestDatabase fixture,
        NativeTextOnlineTestRuntime runtime, OnlineTextIndexMaintenanceRequest request,
        OnlineTextIndexMaintenanceResult receipt, NativeTextCapturedFileRoster files, CancellationToken token)
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
                await NativeTextCapturedPublicContinuation.OriginalAsync(await first, fixture.Partition);
                files.RequireOriginal(fixture, runtime, token);
                var successor = await NativeTextCapturedPublicContinuation.SwapAsync(fixture, runtime, request, token);
                files.RequireOriginal(fixture, runtime, token);
                var successorRequest = request with { CommandId = successor.CommandId };
                var successorFiles = NativeTextCapturedFileRoster.Capture(fixture, runtime, successorRequest, token);
                var retirement = runtime.Owner.CaptureOriginalRetirements().Single();
                if (cancelOriginal)
                { await originalCancellation.CancelAsync(); }
                release.TrySetResult();
                if (cancelOriginal)
                { _ = await Assert.ThrowsAsync<OperationCanceledException>(async () => { _ = await original; }); }
                else
                { await NativeTextCapturedPublicContinuation.OriginalAsync(await original, fixture.Partition); }
                await files.RequireRetirementAsync(retirement, successorFiles, fixture, runtime, successorRequest, token);
                await NativeTextCapturedPublicContinuation.HealthyReplayAsync(fixture, runtime, request, receipt, successor, token);
                successorFiles.RequireCurrent(fixture, runtime, successorRequest, token);
            }, failures);
        }
        finally
        {
            try
            {
                firstRelease.TrySetResult();
                release.TrySetResult();
                if (!first.IsCompleted)
                { await ServerFailureObserver.ObserveAsync(firstCancellation.CancelAsync, failures); }
                await JoinOriginalAsync(original, originalCancellation, cancelOriginal, failures);
            }
            finally
            {
                try
                { _ = await first; }
                catch (Exception error) when (KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(error))
                { RetainFailure(error, failures); }
                catch (Exception error) when (!KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(error))
                { RetainFailure(error, failures); }
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private const string MissingOriginalPosting = "The original public caller completed without a retained native posting observation.";

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

    internal static async Task CancelledCallerThenJoinedAsync(NativeTextOnlineTestRuntime runtime,
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
            { _ = await original; }
            catch (OperationCanceledException) { }
            catch (Exception error) when (KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(error))
            { RetainFailure(error, failures); }
            catch (Exception error) when (!KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(error))
            { RetainFailure(error, failures); }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task JoinOriginalAsync(Task<RankedDocument[]>? original,
        CancellationTokenSource caller, bool cancelOriginal, List<Exception> failures)
    {
        if (original is null)
        { return; }
        if (!original.IsCompleted)
        { await ServerFailureObserver.ObserveAsync(caller.CancelAsync, failures); }
        try
        { _ = await original; }
        catch (OperationCanceledException) when (cancelOriginal && failures.Count == 0) { }
        catch (Exception error) when (KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(error))
        { RetainFailure(error, failures); }
        catch (Exception error) when (!KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(error))
        { RetainFailure(error, failures); }
    }

    private static void RetainFailure(Exception error, List<Exception> failures)
    {
        if (!failures.Any(original => ReferenceEquals(original, error)))
        { failures.Add(error); }
    }

    private static Task<RankedDocument[]> StartOriginal(NativeTextOnlineTestRuntime runtime, TestDatabase fixture,
        TaskCompletionSource entered, TaskCompletionSource release, CancellationToken caller, CancellationToken execution)
        => Task.Run(async () =>
        {
            using var observation = NativeTextPostingObservation.Enter(async cancellation =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(cancellation);
            });
            return await runtime.Search.SearchAsync(NativeTextMaintenanceTestValues.Principal,
                NativeTextBilingualAudit.Request(fixture.Partition, "ПРИВІТ"), caller);
        }, execution);

}
