using Microsoft.Extensions.Options;

namespace KeyLoad.Client;

/// <summary>Bounds successful HTTP JSON input and retained HTTP problem bodies in the execution SDK transport.</summary>
[ConfigurationOptions]
public sealed class KeyLoadClientExecutionOptions
{
    /// <summary>The SDK execution configuration section.</summary>
    public const string SectionName = "KeyLoad:ClientExecution";
    /// <summary>The rejection for an invalid retained response budget.</summary>
    public const string ValidationMessage = "The SDK problem body budget must be positive and at most 64 KiB.";
    private const int DefaultProblemBodyBytes = 65_536;
    private const int MaximumResponseBodyByteCeiling = 16_777_216;
    /// <summary>The rejection for an invalid successful-response budget.</summary>
    public const string ResponseValidationMessage = "The SDK response body budget must be positive and at most 16 MiB.";
    private const int MinimumProblemBodyBytes = 1;
    private const int DefaultSqlInspectionBytes = 65_536;

    /// <summary>The maximum UTF-8 bytes retained when decoding one HTTP problem.</summary>
    public int MaximumProblemBodyBytes { get; set; } = DefaultProblemBodyBytes;
    /// <summary>The inclusive byte ceiling presented to the JSON decoder for one successful response.</summary>
    public int MaximumResponseBodyBytes { get; set; } = MaximumResponseBodyByteCeiling;
    /// <summary>The character ceiling scanned for the first SQL keyword when a failed request is classified as read or write.</summary>
    public int MaximumSqlInspectionBytes { get; set; } = DefaultSqlInspectionBytes;

    /// <summary>Checks the positive budget against its existing protocol ceiling.</summary>
    /// <returns>Whether the SDK can consume this budget.</returns>
    public bool IsValid() => MaximumProblemBodyBytes is >= MinimumProblemBodyBytes and <= DefaultProblemBodyBytes
        && MaximumResponseBodyBytes is >= MinimumProblemBodyBytes and <= MaximumResponseBodyByteCeiling
        && MaximumSqlInspectionBytes is >= MinimumProblemBodyBytes and <= DefaultSqlInspectionBytes;

    /// <summary>Captures and validates the caller's execution values for one client lifetime.</summary>
    [ConfigurationBinding]
    internal static IOptions<KeyLoadClientExecutionOptions> Snapshot(IOptions<KeyLoadClientExecutionOptions> configured)
    {
        ArgumentNullException.ThrowIfNull(configured);
        var value = configured.Value;
        var copy = new KeyLoadClientExecutionOptions
        {
            MaximumProblemBodyBytes = value.MaximumProblemBodyBytes,
            MaximumResponseBodyBytes = value.MaximumResponseBodyBytes,
            MaximumSqlInspectionBytes = value.MaximumSqlInspectionBytes
        };
        copy.Validate();
        return Options.Create(copy);
    }

    /// <summary>Rejects invalid standalone composition before the client can send a request.</summary>
    public void Validate()
    {
        if (!IsValid())
        {
            var detail = MaximumResponseBodyBytes is < MinimumProblemBodyBytes or > MaximumResponseBodyByteCeiling
                ? ResponseValidationMessage : ValidationMessage;
            throw new OptionsValidationException(SectionName, typeof(KeyLoadClientExecutionOptions), [detail]);
        }
    }
}
