namespace KeyLoad.Cli.Features.ClientApi;

/// <summary>The centrally validated deadline for a CLI status request.</summary>
[ConfigurationOptions]
internal sealed class CliExecutionOptions
{
    internal const string ValidationMessage = "The CLI status timeout must be positive and at most two minutes.";
    private const int DefaultStatusTimeoutSeconds = 30;
    private const int MaximumStatusTimeoutMinutes = 2;
    private static readonly TimeSpan MaximumStatusTimeout = TimeSpan.FromMinutes(MaximumStatusTimeoutMinutes);

    internal TimeSpan StatusTimeout { get; set; } = TimeSpan.FromSeconds(DefaultStatusTimeoutSeconds);

    internal bool IsValid() => StatusTimeout > TimeSpan.Zero
        && StatusTimeout <= MaximumStatusTimeout;
}
