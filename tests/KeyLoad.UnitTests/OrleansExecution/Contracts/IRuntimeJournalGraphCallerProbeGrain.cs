namespace KeyLoad.UnitTests.Features.OrleansExecution;

[Alias(RuntimeJournalGraphCallerProtocol.ProbeAlias)]
internal interface IRuntimeJournalGraphCallerProbeGrain : IGrainWithGuidKey
{
    Task<RuntimeJournalGraphCallerResult> ExerciseAsync(CancellationToken cancellationToken);
}
