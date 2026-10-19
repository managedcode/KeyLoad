namespace KeyLoad.Core;

/// <summary>Centrally configured admission for execution signed claim tokens.</summary>
[ConfigurationOptions]
public sealed record ClaimsExecutionOptions
{
    /// <summary>The execution signed claim admission configuration section.</summary>
    public const string SectionName = "KeyLoad:RuntimeClaimsExecution";
    /// <summary>The rejection for an invalid execution signed claim admission policy.</summary>
    public const string ValidationMessage = "Execution signed token admission must be between 1 and 8192 characters.";
    private const int DefaultMaximumTokenCharacters = 8_192;
    private const int MinimumTokenCharacters = 1;

    /// <summary>The maximum accepted token length for implicit execution claim verification.</summary>
    public int MaximumTokenCharacters { get; init; } = DefaultMaximumTokenCharacters;

    /// <summary>Checks the positive admission bound without changing the execution token format.</summary>
    /// <returns>Whether the configured admission fits the existing decoder ceiling.</returns>
    public bool IsValid() => MaximumTokenCharacters is >= MinimumTokenCharacters and <= DefaultMaximumTokenCharacters;

    /// <summary>Rejects invalid policy before execution token processing or physical ownership.</summary>
    public void Validate()
    {
        if (!IsValid())
        {
            throw new InvalidOperationException(ValidationMessage);
        }
    }
}
