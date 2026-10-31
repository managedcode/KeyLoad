using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Search;

/// <summary>Bounds disposable execution text generations and their actual physical inventory and workspace.</summary>
[ConfigurationOptions]
internal sealed class TextExecutionOptions
{
    internal const string SectionName = "KeyLoad:TextExecution";
    internal const string ValidationMessage = "Execution text inventory, generation and workspace settings exceed their accepted ceilings.";
    private const int MaximumFileCeiling = 512;
    private const int MaximumDirectoryCeiling = 32;
    private const int MaximumDepthCeiling = 8;
    private const long MaximumDiskByteCeiling = 268_435_456;
    private const int MaximumMutableSegmentItemCeiling = 4_096;
    private const int MaximumFileBufferByteCeiling = 4_096;
    private const int MaximumGenerationCeiling = 3;
    private const int MaximumOwnerReceiptByteCeiling = 65_536;
    private const int MaximumPostingsCheckInterval = 64;

    public int MaximumFiles { get; set; } = MaximumFileCeiling;
    public int MaximumDirectories { get; set; } = MaximumDirectoryCeiling;
    public int MaximumDepth { get; set; } = MaximumDepthCeiling;
    public long MaximumDiskBytes { get; set; } = MaximumDiskByteCeiling;
    public int MutableSegmentMaximumItems { get; set; } = MaximumMutableSegmentItemCeiling;
    public int FileBufferBytes { get; set; } = MaximumFileBufferByteCeiling;
    public int MaximumGenerations { get; set; } = MaximumGenerationCeiling;
    public int MaximumOwnerReceiptBytes { get; set; } = MaximumOwnerReceiptByteCeiling;
    public int PostingsBoundCheckInterval { get; set; } = MaximumPostingsCheckInterval;
    internal int MaximumEntries => checked(MaximumFiles + MaximumDirectories);

    internal bool IsValid() => MaximumFiles is >= 1 and <= MaximumFileCeiling
        && MaximumDirectories is >= 1 and <= MaximumDirectoryCeiling
        && MaximumDepth is >= 1 and <= MaximumDepthCeiling
        && MaximumDiskBytes is >= 1 and <= MaximumDiskByteCeiling
        && MutableSegmentMaximumItems is >= 1 and <= MaximumMutableSegmentItemCeiling
        && FileBufferBytes is >= 1 and <= MaximumFileBufferByteCeiling
        && MaximumGenerations is >= 1 and <= MaximumGenerationCeiling
        && MaximumOwnerReceiptBytes is >= 1 and <= MaximumOwnerReceiptByteCeiling
        && PostingsBoundCheckInterval is >= 1 and <= MaximumPostingsCheckInterval;

    internal void Validate()
    {
        if (!IsValid())
        {
            throw new OptionsValidationException(SectionName, typeof(TextExecutionOptions), [ValidationMessage]);
        }
    }
}
