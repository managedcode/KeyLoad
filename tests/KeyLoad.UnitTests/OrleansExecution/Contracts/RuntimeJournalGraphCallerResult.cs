namespace KeyLoad.UnitTests.Features.OrleansExecution;

[GenerateSerializer, Alias(RuntimeJournalGraphCallerProtocol.ResultAlias)]
internal sealed record RuntimeJournalGraphCallerResult(
    [property: Id(0)] bool ProviderReadCompleted,
    [property: Id(1)] bool ProviderRestoredScopedCaller,
    [property: Id(2)] bool WrongMethodDenied,
    [property: Id(3)] bool WrongMethodRestoredScopedCaller,
    [property: Id(4)] bool WrongTargetDenied,
    [property: Id(5)] bool WrongTargetRestoredScopedCaller,
    [property: Id(6)] bool ProbeRestoredOriginalCaller);
