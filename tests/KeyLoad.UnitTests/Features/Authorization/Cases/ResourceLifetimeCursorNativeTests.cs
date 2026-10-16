namespace KeyLoad.UnitTests.Features.Authorization;

/// <summary>Actual native scalar/live V2 continuation and signed unsupported historical-input refusal.</summary>
internal sealed class ResourceLifetimeCursorNativeTests
{
    [Test]
    public Task GenuineScalarAndLiveCursorsBindCreationRejectOldFormatAndAnotherActualCreationThenHealthyAndTwoCold()
        => SafeProjectorOperationDatabase.RunAsync(owner => SafeProjectorOperationDatabase.RunAsync(async foreign =>
        {
            var first = await SafeProjectorOperationSeed.CreateAsync(owner);
            var second = await SafeProjectorOperationSeed.CreateAsync(foreign);
            await ResourceLifetimeCursorNativeFlow.RunAsync(owner, first, second);
        }, owner.CallerToken), TestContext.Current!.Execution.CancellationToken);
}
