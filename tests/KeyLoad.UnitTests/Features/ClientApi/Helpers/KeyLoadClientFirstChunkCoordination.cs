namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>Joins the actual first-chunk/handler/native SDK race under its original caller-owned wait bound.</summary>
internal static class KeyLoadClientFirstChunkCoordination
{
    internal static async Task WaitForFirstChunkAsync(Task firstChunkWritten, Task handlerEntered,
        Task<(FirstRequestStage Stage, Exception Error)> handlerFailure,
        Task<ManagedCode.Communication.Result<NodeStatus>> pending, Func<FirstRequestStage> currentStage, TimeSpan timeout)
    {
        try
        {
            var completed = await Task.WhenAny(firstChunkWritten, handlerFailure, pending)
                .WaitAsync(timeout, TimeProvider.System);
            if (completed == pending && !firstChunkWritten.IsCompleted)
            {
                var result = await pending;
                throw new InvalidOperationException(
                    $"SDK request completed before first Kestrel chunk; handler entered: {handlerEntered.IsCompleted}; stage: {currentStage()}; success: {result.IsSuccess}; code: {result.Problem?.ErrorCode}.");
            }
            if (completed == handlerFailure)
            {
                var failure = await handlerFailure;
                throw new InvalidOperationException(
                    $"Kestrel first response failed during {failure.Stage}.", failure.Error);
            }
        }
        catch (TimeoutException exception)
        {
            throw new TimeoutException(
                $"Timed out waiting for the first Kestrel response chunk; handler entered: {handlerEntered.IsCompleted}; stage: {currentStage()}; SDK completed: {pending.IsCompleted}; SDK status: {pending.Status}.",
                exception);
        }
    }
}
