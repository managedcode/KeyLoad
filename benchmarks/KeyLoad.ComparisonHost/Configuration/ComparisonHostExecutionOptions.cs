
namespace KeyLoad.ComparisonHost.Features.BenchmarkComparisons;

[ConfigurationOptions]
internal sealed class ComparisonHostExecutionOptions
{
    internal const string SectionName = "Benchmarks:HostExecution";
    internal const string InvalidSettings = "The comparison host input and file execution limits are invalid.";
    private const int MaximumFileBuffer = 65536;
    private const int MaximumImageInput = 1024;

    public int FileBufferBytes { get; set; } = MaximumFileBuffer;
    public int MaximumImageReferenceCharacters { get; set; } = MaximumImageInput;
    internal bool IsValid() => FileBufferBytes is >= 1 and <= MaximumFileBuffer
        && MaximumImageReferenceCharacters is >= 1 and <= MaximumImageInput;
}
