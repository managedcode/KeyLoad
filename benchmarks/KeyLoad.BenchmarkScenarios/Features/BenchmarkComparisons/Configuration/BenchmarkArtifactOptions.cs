namespace KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

/// <summary>Captures explicit output selection and original source provenance for generated-runner artifacts.</summary>
[ConfigurationOptions]
internal sealed class BenchmarkArtifactOptions
{
    internal const string SectionName = "BenchmarkArtifacts";
    internal const string InvalidSource = "The sample chunk measurement source identity is missing or malformed.";
    private const int GitShaLength = 40;
    private const int Sha256Length = 64;
    public string? NativeSerializationDirectory { get; set; }
    public string? SampleChunkDirectory { get; set; }
    public string? SourceHead { get; set; }
    public string? SourceInventorySha256 { get; set; }

    internal bool IsValid()
    {
        if (string.IsNullOrWhiteSpace(SampleChunkDirectory)) { return true; }
        if (!IsHex(SourceHead, GitShaLength) || !IsHex(SourceInventorySha256, Sha256Length))
        { throw new InvalidOperationException(InvalidSource); }
        return true;
    }

    private static bool IsHex(string? value, int length)
        => value is not null && value.Length == length && value.All(char.IsAsciiHexDigit);
}
