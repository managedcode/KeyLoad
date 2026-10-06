using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

[ConfigurationOptions]
internal sealed record OpenLoopPlanProcessOptions
{
    internal const string SectionName = "KeyLoad:OpenLoopPlanProcess";
    internal const string InvalidOptionsMessage = "Open-loop plan process limits are outside their accepted bounds.";
    private const int DefaultProcessTimeoutSeconds = 20;
    private const int MaximumProcessTimeoutSeconds = 60;
    private const int MaximumInputByteLimit = 1_048_576;
    private const int MaximumOutputCharacterLimit = 1_048_576;
    private const int DefaultStreamBufferCharacters = 4_096;
    private const int MinimumStreamBufferCharacters = 1_024;
    private const int MaximumStreamBufferCharacters = 16_384;
    private const int MinimumPositiveLimit = 1;

    internal TimeSpan ProcessTimeout { get; init; } = TimeSpan.FromSeconds(DefaultProcessTimeoutSeconds);
    internal int MaximumInputBytes { get; init; } = MaximumInputByteLimit;
    internal int MaximumOutputCharacters { get; init; } = MaximumOutputCharacterLimit;
    internal int StreamBufferCharacters { get; init; } = DefaultStreamBufferCharacters;

    internal bool IsValid() => ProcessTimeout > TimeSpan.Zero
        && ProcessTimeout <= TimeSpan.FromSeconds(MaximumProcessTimeoutSeconds)
        && MaximumInputBytes is >= MinimumPositiveLimit and <= MaximumInputByteLimit
        && MaximumOutputCharacters is >= MinimumPositiveLimit and <= MaximumOutputCharacterLimit
        && StreamBufferCharacters is >= MinimumStreamBufferCharacters and <= MaximumStreamBufferCharacters;

    internal void Validate()
    {
        if (!IsValid())
        {
            throw new OptionsValidationException(SectionName, typeof(OpenLoopPlanProcessOptions), [InvalidOptionsMessage]);
        }
    }
}
