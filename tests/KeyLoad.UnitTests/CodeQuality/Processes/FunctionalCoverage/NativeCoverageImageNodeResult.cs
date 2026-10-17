namespace KeyLoad.UnitTests.Features.CodeQuality;

internal sealed record NativeCoverageImageNodeResult(int ExitCode, string StandardOutput,
    string StandardError, bool OriginalExitJoined, bool StandardOutputJoined,
    bool StandardErrorJoined, bool ProcessDisposed);
