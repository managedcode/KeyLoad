namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class RuntimeJournalGraphCallerProtocol
{
    internal const string ProbeAlias = "keyload.tests.runtime-journal-graph-caller-probe.v1";
    internal const string ResultAlias = "keyload.tests.runtime-journal-graph-caller-result.v1";
}

[Alias(RuntimeJournalGraphCallerProtocol.ProbeAlias)]
internal interface IRuntimeJournalGraphCallerProbeGrain : IGrainWithGuidKey
{
    Task<RuntimeJournalGraphCallerResult> ExerciseAsync(CancellationToken cancellationToken);
}

[GenerateSerializer, Alias(RuntimeJournalGraphCallerProtocol.ResultAlias)]
internal sealed record RuntimeJournalGraphCallerResult(
    [property: Id(0)] bool ProviderReadCompleted,
    [property: Id(1)] bool ProviderRestoredScopedCaller,
    [property: Id(2)] bool WrongMethodDenied,
    [property: Id(3)] bool WrongMethodRestoredScopedCaller,
    [property: Id(4)] bool WrongTargetDenied,
    [property: Id(5)] bool WrongTargetRestoredScopedCaller,
    [property: Id(6)] bool ProbeRestoredOriginalCaller);
