using KeyLoad;

namespace KeyLoad.AppHost.Features.ClusterReplication;

/// <summary>Bounded private profile parsing and offline command input policy.</summary>
[ConfigurationOptions]
internal sealed record ClusterProfileExecutionOptions
{
    private const int MaximumBytes = 8192;
    private const int MaximumDepth = 8;
    private const int MaximumPath = 4096;
    private const int MinimumPositive = 1;
    private const int MaximumBufferBytes = 4096;
    internal const string SectionName = "KeyLoad:ClusterProfileExecution";
    internal const string ValidationMessage = "Private cluster profile execution settings are invalid.";
    public int MaximumProfileBytes { get; init; } = MaximumBytes;
    public int MaximumJsonDepth { get; init; } = MaximumDepth;
    public int MaximumPathCharacters { get; init; } = MaximumPath;
    public int FileBufferBytes { get; init; } = MaximumBufferBytes;
    internal bool IsValid() => MaximumProfileBytes is >= MinimumPositive and <= MaximumBytes
        && MaximumJsonDepth is >= MinimumPositive and <= MaximumDepth
        && MaximumPathCharacters is >= MinimumPositive and <= MaximumPath
        && FileBufferBytes is >= MinimumPositive and <= MaximumBufferBytes;
}
