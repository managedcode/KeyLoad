namespace KeyLoad.UnitTests.Features.ClusterRouting;

[Alias(RuntimeJournalGraphCallerProtocol.ProbeAlias)]
internal interface IRuntimeJournalGraphCallerProbeGrain : IGrainWithGuidKey
{
    Task<RuntimeJournalGraphCallerResult> ExerciseAsync(CancellationToken cancellationToken);
}
