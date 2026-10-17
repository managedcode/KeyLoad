namespace KeyLoad.IntegrationTests.Features.Authorization;

/// <summary>Frozen bounded policy for the native dashboard RF3 observation.</summary>
[ConfigurationOptions]
internal sealed class FoundationOtlpRf3ExecutionOptions
{
    private const int ResponseCeiling = 8_388_608;
    private const int RecordsCeiling = 4_096;
    private const int PrivateTokenCeiling = 4_096;
    private const int AttemptsCeiling = 80;
    private const int DepthCeiling = 32;
    private const int ChunkCeiling = 8_192;
    private const int ObservationSeconds = 30;
    private const int RequestSeconds = 10;
    private const int PollMilliseconds = 250;
    private const string InvalidPolicy = "The RF3 telemetry observation policy exceeds its frozen bounds.";

    public int MaximumResponseBytes { get; init; } = ResponseCeiling;
    public int MaximumRecords { get; init; } = RecordsCeiling;
    public int MaximumPrivateTokenCharacters { get; init; } = PrivateTokenCeiling;
    public int MaximumAttempts { get; init; } = AttemptsCeiling;
    public int MaximumJsonDepth { get; init; } = DepthCeiling;
    public int ReadChunkBytes { get; init; } = ChunkCeiling;
    public TimeSpan ObservationTimeout { get; init; } = TimeSpan.FromSeconds(ObservationSeconds);
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(RequestSeconds);
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(PollMilliseconds);

    internal void Validate()
    {
        if (!PositiveAtMost(MaximumResponseBytes, ResponseCeiling)
            || !PositiveAtMost(MaximumRecords, RecordsCeiling)
            || !PositiveAtMost(MaximumPrivateTokenCharacters, PrivateTokenCeiling)
            || !PositiveAtMost(MaximumAttempts, AttemptsCeiling)
            || !PositiveAtMost(MaximumJsonDepth, DepthCeiling)
            || !PositiveAtMost(ReadChunkBytes, ChunkCeiling)
            || ObservationTimeout <= TimeSpan.Zero || ObservationTimeout > TimeSpan.FromSeconds(ObservationSeconds)
            || RequestTimeout <= TimeSpan.Zero || RequestTimeout > TimeSpan.FromSeconds(RequestSeconds)
            || PollInterval != TimeSpan.FromMilliseconds(PollMilliseconds))
        {
            throw new InvalidOperationException(InvalidPolicy);
        }
    }

    private static bool PositiveAtMost(int value, int maximum) => value > 0 && value <= maximum;
}
