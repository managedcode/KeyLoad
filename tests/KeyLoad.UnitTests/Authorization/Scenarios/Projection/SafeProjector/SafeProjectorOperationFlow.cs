namespace KeyLoad.UnitTests.Authorization;

internal static class SafeProjectorOperationFlow
{
    internal static async Task RunAsync(SafeProjectorOperationDatabase owner)
    {
        var history = new SafeProjectorOperationHistory(await SafeProjectorOperationSeed.CreateAsync(owner));
        await SafeProjectorOperationInitialFlow.RunAsync(owner, history);
        await SafeProjectorOperationContinuation.RunAsync(owner, history);
        await SafeProjectorOperationCold.RunAsync(owner, history);
    }
}
