namespace KeyLoad.UnitTests.Features.CodeQuality.Processes;

internal sealed record ProductionSourceManifestProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool OriginalExitJoined,
    bool StandardOutputJoined,
    bool StandardErrorJoined,
    bool ProcessDisposed);
