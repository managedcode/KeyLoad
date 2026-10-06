
namespace KeyLoad.AppHost.Features.ClusterReplication;

/// <summary>Bounded identity parsing before a runtime container can be admitted.</summary>
[ConfigurationOptions]
internal sealed class ContainerImageExecutionOptions
{
    internal const string SectionName = "KeyLoad:ContainerImageExecution";
    internal const string ValidationMessage = "Container image parsing limits exceed their supported bounds.";
    private const int MaximumImageLength = 1024;
    private const int MaximumLocalLength = 256;
    private const int MaximumTimeoutMilliseconds = 100;
    private const int MinimumPositive = 1;
    public int MaximumImageCharacters { get; set; } = MaximumImageLength;
    public int MaximumLocalImageCharacters { get; set; } = MaximumLocalLength;
    public int MaximumReceiptPathCharacters { get; set; } = MaximumLocalLength;
    public TimeSpan MatchTimeout { get; set; } = TimeSpan.FromMilliseconds(MaximumTimeoutMilliseconds);
    internal bool IsValid() => MaximumImageCharacters is >= MinimumPositive and <= MaximumImageLength
        && MaximumLocalImageCharacters is >= MinimumPositive and <= MaximumLocalLength
        && MaximumReceiptPathCharacters is >= MinimumPositive and <= MaximumLocalLength
        && MatchTimeout > TimeSpan.Zero && MatchTimeout.Ticks <= MaximumTimeoutMilliseconds * TimeSpan.TicksPerMillisecond;
}
