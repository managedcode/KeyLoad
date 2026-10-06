using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

/// <summary>Bounds disposable native text generations and their actual physical inventory and workspace.</summary>
[ConfigurationOptions]
internal sealed class NativeTextExecutionOptions
{
    internal const string SectionName = "KeyLoad:NativeTextExecution";
    internal const string ValidationMessage = "Native text inventory, generation and workspace settings exceed their accepted ceilings.";
    private const int MinimumPositiveBudget = 1;
    private const int MaximumFileCeiling = 512;
    private const int MaximumDirectoryCeiling = 32;
    private const int MaximumDepthCeiling = 8;
    private const long MaximumDiskByteCeiling = 268_435_456;
    private const int MaximumMutableSegmentItemCeiling = 4_096;
    private const int MaximumHashBufferByteCeiling = 65_536;
    private const int MaximumFileBufferByteCeiling = 4_096;
    private const int MaximumGenerationCeiling = 3;
    private const int MaximumOwnerReceiptByteCeiling = 65_536;
    private const int MaximumActiveLeaseCeiling = 2;
    private const int MaximumPostingsCheckInterval = 64;

    public int MaximumFiles { get; set; } = MaximumFileCeiling;
    public int MaximumDirectories { get; set; } = MaximumDirectoryCeiling;
    public int MaximumDepth { get; set; } = MaximumDepthCeiling;
    public long MaximumDiskBytes { get; set; } = MaximumDiskByteCeiling;
    public int MutableSegmentMaximumItems { get; set; } = MaximumMutableSegmentItemCeiling;
    public int HashBufferBytes { get; set; } = MaximumHashBufferByteCeiling;
    public int FileBufferBytes { get; set; } = MaximumFileBufferByteCeiling;
    public int MaximumGenerations { get; set; } = MaximumGenerationCeiling;
    public int MaximumOwnerReceiptBytes { get; set; } = MaximumOwnerReceiptByteCeiling;
    public int PostingsBoundCheckInterval { get; set; } = MaximumPostingsCheckInterval;
    public int MaximumActiveLeases { get; set; } = MaximumActiveLeaseCeiling;
    internal int MaximumEntries => checked(MaximumFiles + MaximumDirectories);

    internal bool IsValid() => MaximumFiles is >= MinimumPositiveBudget and <= MaximumFileCeiling
        && MaximumDirectories is >= MinimumPositiveBudget and <= MaximumDirectoryCeiling
        && MaximumDepth is >= MinimumPositiveBudget and <= MaximumDepthCeiling
        && MaximumDiskBytes is >= MinimumPositiveBudget and <= MaximumDiskByteCeiling
        && MutableSegmentMaximumItems is >= MinimumPositiveBudget and <= MaximumMutableSegmentItemCeiling
        && HashBufferBytes is >= MinimumPositiveBudget and <= MaximumHashBufferByteCeiling
        && FileBufferBytes is >= MinimumPositiveBudget and <= MaximumFileBufferByteCeiling
        && MaximumGenerations is >= MinimumPositiveBudget and <= MaximumGenerationCeiling
        && MaximumOwnerReceiptBytes is >= MinimumPositiveBudget and <= MaximumOwnerReceiptByteCeiling
        && MaximumActiveLeases is >= MinimumPositiveBudget and <= MaximumActiveLeaseCeiling
        && PostingsBoundCheckInterval is >= MinimumPositiveBudget and <= MaximumPostingsCheckInterval;

    internal void Validate()
    {
        if (!IsValid())
        { throw new OptionsValidationException(SectionName, typeof(NativeTextExecutionOptions), [ValidationMessage]); }
    }
}
