namespace KeyLoad.UnitTests.BackupRestore;

internal sealed class ManifestCutRepairTests
{
    [Test, Arguments(false), Arguments(true)]
    public Task InvalidManifestPositionRefusesAndCliRestoreThenExactRepairPreservesTwoColdCuts(bool declaredCutMismatch)
        => ManifestCutRepairTrial.RunAsync(declaredCutMismatch, TestContext.Current!.Execution.CancellationToken);
}
