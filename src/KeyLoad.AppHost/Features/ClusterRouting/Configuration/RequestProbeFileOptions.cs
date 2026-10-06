using KeyLoad;

namespace KeyLoad.AppHost.Features.ClusterRouting;

/// <summary>Private probe owner-file and canonical-path execution budgets.</summary>
[ConfigurationOptions]
internal sealed record RequestProbeFileOptions
{
    private const int MaximumOwnerCapacity = 8192;
    private const int MaximumBufferCapacity = 4096;
    private const int MaximumPathCapacity = 4096;
    private const int MinimumPositive = 1;
    internal const string SectionName = "KeyLoadTests:RequestProbeFileExecution";
    internal const string ValidationMessage = "Private request probe file policy exceeds its bounds.";
    public int MaximumOwnerBytes { get; init; } = MaximumOwnerCapacity;
    public int NativeReadBufferBytes { get; init; } = MaximumBufferCapacity;
    public int MaximumPathCharacters { get; init; } = MaximumPathCapacity;
    internal bool IsValid() => MaximumOwnerBytes is >= MinimumPositive and <= MaximumOwnerCapacity
        && NativeReadBufferBytes is >= MinimumPositive and <= MaximumBufferCapacity
        && MaximumPathCharacters is >= MinimumPositive and <= MaximumPathCapacity;
}
