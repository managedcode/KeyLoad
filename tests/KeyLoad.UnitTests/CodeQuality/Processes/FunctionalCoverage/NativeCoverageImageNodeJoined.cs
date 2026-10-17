namespace KeyLoad.UnitTests.Features.CodeQuality;

internal sealed record NativeCoverageImageNodeJoined(int ExitCode, string StandardOutput,
    string StandardError, bool ExitJoined, bool OutputJoined, bool ErrorJoined)
{
    internal static NativeCoverageImageNodeJoined Empty
    {
        get;
    } = new(-1, string.Empty, string.Empty,
        false, false, false);
}
