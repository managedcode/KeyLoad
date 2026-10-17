namespace KeyLoad.UnitTests.Features.BackupRestore;

internal sealed class CurrentJournalSignatureRepairTests
{
    [Test, Arguments(false), Arguments(true)]
    public Task UnknownCurrentNativeJournalSignatureRefusesWithoutEffectsThenExactRepairAndHealthyTwoCold(bool checkpoint)
        => CurrentJournalSignatureRepairFlow.RunAsync(checkpoint, TestContext.Current!.Execution.CancellationToken);
}
