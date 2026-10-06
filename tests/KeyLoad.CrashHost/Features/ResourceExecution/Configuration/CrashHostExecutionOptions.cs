using Microsoft.Extensions.Options;

namespace KeyLoad.CrashHost;

/// <summary>Bounded execution policy for the original crash-helper child processes.</summary>
[ConfigurationOptions]
internal sealed record CrashHostExecutionOptions
{
    internal const string SectionName = "KeyLoad:CrashHostExecution";
    internal const string ValidationMessage = "Crash-helper execution policy is outside its supported bounds.";
    private const int PositiveMinimum = 1;
    private const int FifoTimeoutSeconds = 5;
    private const int InspectionExecutionSeconds = 30;
    private const int InspectionCleanupSeconds = 15;
    private const int EpochApplySeconds = 30;
    private const int SupportedOutputCharacters = 4096;
    private const int SupportedReadCharacters = 256;
    private const int SupportedInspectionOutputBytes = 4096;
    private const int SupportedInspectionReadBytes = 1024;
    private const int SupportedAuthorityReadBytes = 4096;
    private const int SupportedProfileReadCharacters = 256;
    private const int SupportedInspectionInputBytes = 1024;

    public TimeSpan FifoExecutionTimeout { get; init; } = TimeSpan.FromSeconds(FifoTimeoutSeconds);
    public TimeSpan FifoCleanupTimeout { get; init; } = TimeSpan.FromSeconds(FifoTimeoutSeconds);
    public TimeSpan InspectionExecutionTimeout { get; init; } = TimeSpan.FromSeconds(InspectionExecutionSeconds);
    public TimeSpan InspectionCleanupTimeout { get; init; } = TimeSpan.FromSeconds(InspectionCleanupSeconds);
    public TimeSpan EpochApplyTimeout { get; init; } = TimeSpan.FromSeconds(EpochApplySeconds);
    public int FifoMaximumOutputCharacters { get; init; } = SupportedOutputCharacters;
    public int FifoReadChunkCharacters { get; init; } = SupportedReadCharacters;
    public int InspectionMaximumOutputBytes { get; init; } = SupportedInspectionOutputBytes;
    public int InspectionReadBufferBytes { get; init; } = SupportedInspectionReadBytes;
    public int ProfileReadChunkCharacters { get; init; } = SupportedProfileReadCharacters;
    public int InspectionInputChunkBytes { get; init; } = SupportedInspectionInputBytes;
    public int AuthorityReadBufferBytes { get; init; } = SupportedAuthorityReadBytes;

    internal bool IsValid()
        => Positive(FifoExecutionTimeout, FifoTimeoutSeconds) && Positive(FifoCleanupTimeout, FifoTimeoutSeconds)
            && Positive(InspectionExecutionTimeout, InspectionExecutionSeconds)
            && Positive(InspectionCleanupTimeout, InspectionCleanupSeconds) && Positive(EpochApplyTimeout, EpochApplySeconds)
            && FifoMaximumOutputCharacters is >= PositiveMinimum and <= SupportedOutputCharacters
            && FifoReadChunkCharacters is >= PositiveMinimum and <= SupportedReadCharacters
            && FifoReadChunkCharacters <= FifoMaximumOutputCharacters
            && InspectionMaximumOutputBytes is >= PositiveMinimum and <= SupportedInspectionOutputBytes
            && InspectionReadBufferBytes is >= PositiveMinimum and <= SupportedInspectionReadBytes
            && InspectionReadBufferBytes <= InspectionMaximumOutputBytes
            && ProfileReadChunkCharacters is >= PositiveMinimum and <= SupportedProfileReadCharacters
            && InspectionInputChunkBytes is >= PositiveMinimum and <= SupportedInspectionInputBytes
            && AuthorityReadBufferBytes is >= PositiveMinimum and <= SupportedAuthorityReadBytes;

    private static bool Positive(TimeSpan value, int maximumSeconds)
        => value > TimeSpan.Zero && value.Ticks <= maximumSeconds * TimeSpan.TicksPerSecond;
}

internal sealed class CrashHostExecutionOptionsValidator : IValidateOptions<CrashHostExecutionOptions>
{
    public ValidateOptionsResult Validate(string? name, CrashHostExecutionOptions options)
        => options.IsValid() ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(CrashHostExecutionOptions.ValidationMessage);
}
