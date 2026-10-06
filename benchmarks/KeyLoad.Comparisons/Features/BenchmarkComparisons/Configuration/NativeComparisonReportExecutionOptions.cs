namespace KeyLoad.Comparisons;

/// <summary>Report buffering consumed through the centrally validated native execution policy.</summary>
public sealed partial class NativeComparisonExecutionOptions
{
    private const int DefaultReportWriterBufferCharacters = 16_384;
    private const int DefaultOpenLoopEvidenceBufferBytes = 16_384;
    private const int DefaultOpenLoopProofBufferBytes = 8_192;
    private const int DefaultOpenLoopControlFileBufferBytes = 11;

    /// <summary>The character buffer used by the native CSV writer.</summary>
    public int ReportWriterBufferCharacters { get; set; } = DefaultReportWriterBufferCharacters;
    /// <summary>The byte buffer used by the native open-loop report writer.</summary>
    public int OpenLoopEvidenceBufferBytes { get; set; } = DefaultOpenLoopEvidenceBufferBytes;
    /// <summary>The byte buffer used by the native cancellation proof writer.</summary>
    public int OpenLoopProofBufferBytes { get; set; } = DefaultOpenLoopProofBufferBytes;
    /// <summary>The native stream buffer used while validating the fixed cancellation request frame.</summary>
    public int OpenLoopControlFileBufferBytes { get; set; } = DefaultOpenLoopControlFileBufferBytes;

    private void ValidateReportPolicy()
    {
        if (ReportWriterBufferCharacters is <= MinimumPositiveLimit or > DefaultReportWriterBufferCharacters
            || OpenLoopEvidenceBufferBytes is <= MinimumPositiveLimit or > DefaultOpenLoopEvidenceBufferBytes
            || OpenLoopProofBufferBytes is <= MinimumPositiveLimit or > DefaultOpenLoopProofBufferBytes
            || OpenLoopControlFileBufferBytes is <= MinimumPositiveLimit or > DefaultOpenLoopControlFileBufferBytes)
        {
            throw new Microsoft.Extensions.Options.OptionsValidationException(SectionName, typeof(NativeComparisonExecutionOptions),
                [NativeComparisonOperationalLimitsMustBePresentPositive]);
        }
    }
}
