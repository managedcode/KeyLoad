namespace KeyLoad.Core;

/// <summary>Centrally configured admission for native signed claim tokens.</summary>
[ConfigurationOptions]
public sealed record NativeClaimsExecutionOptions
{
    /// <summary>The native signed claim admission configuration section.</summary>
    public const string SectionName = "KeyLoad:NativeClaimsExecution";
    /// <summary>The rejection for an invalid native signed claim admission policy.</summary>
    public const string ValidationMessage = "Native signed token admission must be between 1 and 8192 characters.";
    private const int DefaultMaximumTokenCharacters = 8_192;
    private const int MinimumTokenCharacters = 1;

    /// <summary>The maximum accepted token length for implicit native claim verification.</summary>
    public int MaximumTokenCharacters { get; init; } = DefaultMaximumTokenCharacters;

    /// <summary>Checks the positive admission bound without changing the native token format.</summary>
    /// <returns>Whether the configured admission fits the existing decoder ceiling.</returns>
    public bool IsValid() => MaximumTokenCharacters is >= MinimumTokenCharacters and <= DefaultMaximumTokenCharacters;

    /// <summary>Rejects invalid policy before native token processing or physical ownership.</summary>
    public void Validate()
    {
        if (!IsValid())
        {
            throw new InvalidOperationException(ValidationMessage);
        }
    }
}
