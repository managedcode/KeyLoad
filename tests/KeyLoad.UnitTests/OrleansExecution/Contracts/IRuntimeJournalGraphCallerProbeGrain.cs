namespace KeyLoad.UnitTests.Features.OrleansExecution;

[Alias(RuntimeJournalGraphCallerProtocol.ProbeAlias)]
[global::Orleans.CodeGeneration.Version(1)]
internal interface IRuntimeJournalGraphCallerProbeGrain : IGrainWithGuidKey
{
    Task<RuntimeJournalGraphCallerResult> ExerciseAsync(CancellationToken cancellationToken);
}
