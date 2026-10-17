namespace KeyLoad.UnitTests.Features.BackupRestore;

internal sealed class ManifestCutRepairTests
{
    [Test, Arguments(false), Arguments(true)]
    public Task InvalidManifestPositionRefusesNativeAndCliRestoreThenExactRepairPreservesTwoColdCuts(bool declaredCutMismatch)
        => ManifestCutRepairTrial.RunAsync(declaredCutMismatch, TestContext.Current!.Execution.CancellationToken);
}
