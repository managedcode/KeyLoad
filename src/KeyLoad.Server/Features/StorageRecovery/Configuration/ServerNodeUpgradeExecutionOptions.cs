using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

/// <summary>Bounds finite stopped-node inventory and file workspace before ownership and conversion.</summary>
[ConfigurationOptions]
internal sealed class ServerNodeUpgradeExecutionOptions
{
    internal const string SectionName = "KeyLoad:NodeUpgrade";
    internal const string ValidationMessage = "Stopped-node upgrade inventory and file workspace limits exceed their accepted bounds.";
    private const int MinimumPositiveBudget = 1;
    private const int MaximumEntryCeiling = 110_000;
    private const int MaximumFileCeiling = 100_000;
    private const int MaximumDirectoryCeiling = 10_000;
    private const int MaximumDepthCeiling = 64;
    private const int MaximumPathCharacterCeiling = 4_194_304;
    private const long MaximumSourceByteCeiling = 549_755_813_888;
    private const int MaximumFileBufferByteCeiling = 65_536;

    public int MaximumEntries { get; set; } = MaximumEntryCeiling;
    public int MaximumFiles { get; set; } = MaximumFileCeiling;
    public int MaximumDirectories { get; set; } = MaximumDirectoryCeiling;
    public int MaximumDepth { get; set; } = MaximumDepthCeiling;
    public int MaximumTotalPathCharacters { get; set; } = MaximumPathCharacterCeiling;
    public long MaximumSourceBytes { get; set; } = MaximumSourceByteCeiling;
    public int FileBufferBytes { get; set; } = MaximumFileBufferByteCeiling;

    internal bool IsValid() => MaximumEntries is >= MinimumPositiveBudget and <= MaximumEntryCeiling
        && MaximumFiles is >= MinimumPositiveBudget and <= MaximumFileCeiling
        && MaximumDirectories is >= MinimumPositiveBudget and <= MaximumDirectoryCeiling
        && MaximumFiles <= MaximumEntries - MaximumDirectories
        && MaximumDepth is >= MinimumPositiveBudget and <= MaximumDepthCeiling
        && MaximumTotalPathCharacters is >= MinimumPositiveBudget and <= MaximumPathCharacterCeiling
        && MaximumSourceBytes is >= MinimumPositiveBudget and <= MaximumSourceByteCeiling
        && FileBufferBytes is >= MinimumPositiveBudget and <= MaximumFileBufferByteCeiling;

    internal void Validate()
    {
        if (!IsValid())
        { throw new OptionsValidationException(SectionName, typeof(ServerNodeUpgradeExecutionOptions), [ValidationMessage]); }
    }
}
