namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed partial record NativeComparisonHarnessOptions
{
    private const int ComparisonLogLines = 150, NodeLogLines = 2000, ResourceLogNameCharacters = 100;
    private const int ResourceLogBytes = 1_048_576, ResourceLogLineBytes = 16_384;
    private const int CancellationFileBufferBytes = 32, CaptureDisposalSeconds = 30;

    public int MaximumRetainedComparisonLogLines { get; init; } = ComparisonLogLines;
    public int MaximumRetainedNodeLogLines { get; init; } = NodeLogLines;
    public int MaximumResourceLogBytes { get; init; } = ResourceLogBytes;
    public int MaximumLogLineBytes { get; init; } = ResourceLogLineBytes;
    public int MaximumResourceLogNameCharacters { get; init; } = ResourceLogNameCharacters;
    public int CancellationRequestFileBufferBytes { get; init; } = CancellationFileBufferBytes;
    public TimeSpan ConcurrentCaptureDisposalTimeout { get; init; } = TimeSpan.FromSeconds(CaptureDisposalSeconds);

    private bool IsLogPolicyValid()
        => MaximumRetainedComparisonLogLines is >= PositiveMinimum and <= ComparisonLogLines
            && MaximumRetainedNodeLogLines is >= PositiveMinimum and <= NodeLogLines
            && MaximumResourceLogBytes is >= PositiveMinimum and <= ResourceLogBytes
            && MaximumLogLineBytes is >= PositiveMinimum and <= ResourceLogLineBytes
            && MaximumLogLineBytes <= MaximumResourceLogBytes
            && MaximumResourceLogNameCharacters is >= PositiveMinimum and <= ResourceLogNameCharacters
            && CancellationRequestFileBufferBytes is >= PositiveMinimum and <= CancellationFileBufferBytes
            && PositiveSeconds(ConcurrentCaptureDisposalTimeout, CaptureDisposalSeconds);
}
