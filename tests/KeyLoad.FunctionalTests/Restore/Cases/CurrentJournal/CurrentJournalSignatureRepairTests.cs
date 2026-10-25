namespace KeyLoad.FunctionalTests.BackupRestore;

internal sealed class CurrentJournalSignatureRepairTests
{
    [Test, Arguments(false), Arguments(true)]
    public Task UnknownCurrentJournalSignatureRefusesWithoutEffectsThenExactRepairAndHealthyTwoCold(bool checkpoint)
        => CurrentJournalSignatureRepairFlow.RunAsync(checkpoint, TestContext.Current!.Execution.CancellationToken);
}
