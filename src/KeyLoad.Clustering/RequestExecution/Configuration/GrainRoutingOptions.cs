using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

/// <summary>Centrally configured lifetime of an idle client transport.</summary>
[ConfigurationOptions]
public sealed class GrainRoutingOptions
{
    /// <summary>The centrally bound request envelope execution section.</summary>
    public const string SectionName = "KeyLoad:GrainRouting";
    /// <summary>The startup failure for unsafe request and execution lifetimes.</summary>
    public const string ValidationMessage = "The idle connection timeout must be positive and at most two minutes.";

    internal const string MaximumExecutionLifetime = "00:02:00";
    private static readonly TimeSpan MaximumDuration = TimeSpan.Parse(MaximumExecutionLifetime, System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Gets or sets idle physical transport expiry while no operation is active.</summary>
    public TimeSpan ConnectionIdleTimeout { get; set; } = MaximumDuration;

    /// <summary>Checks the idle connection timeout before startup.</summary>
    /// <returns>Whether the configured interval is positive and bounded.</returns>
    public bool IsValid() => IsBounded(ConnectionIdleTimeout);

    /// <summary>Rejects an invalid captured routing snapshot before execution execution.</summary>
    public void Validate()
    {
        if (!IsValid())
        {
            throw new OptionsValidationException(SectionName, typeof(GrainRoutingOptions), [ValidationMessage]);
        }
    }

    private static bool IsBounded(TimeSpan value) => value > TimeSpan.Zero && value <= MaximumDuration;
}
