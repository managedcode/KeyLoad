using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

/// <summary>Centrally configured lifetimes for issued request envelopes and their joined execution execution streams.</summary>
[ConfigurationOptions]
public sealed class GrainRoutingOptions
{
    /// <summary>The centrally bound request envelope execution section.</summary>
    public const string SectionName = "KeyLoad:GrainRouting";
    /// <summary>The startup failure for unsafe request and execution lifetimes.</summary>
    public const string ValidationMessage = "Grain request lifetimes must be positive, at most two minutes, and issuance must fit the accepted future window.";

    internal const string MaximumExecutionLifetime = "00:02:00";
    private static readonly TimeSpan MaximumDuration = TimeSpan.Parse(MaximumExecutionLifetime, System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Gets or sets the expiry interval stamped on newly issued envelopes.</summary>
    public TimeSpan RequestLifetime { get; set; } = TimeSpan.FromMinutes(1);
    /// <summary>Gets or sets the accepted future expiry window during request validation.</summary>
    public TimeSpan MaximumFuture { get; set; } = MaximumDuration;

    /// <summary>Gets or sets idle physical transport expiry while no operation is active.</summary>
    public TimeSpan ConnectionIdleTimeout { get; set; } = MaximumDuration;

    /// <summary>Checks the bounded issuance and execution lifecycle before startup.</summary>
    /// <returns>Whether every configured interval preserves the accepted expiry bounds.</returns>
    public bool IsValid() => IsBounded(RequestLifetime) && IsBounded(MaximumFuture)
        && RequestLifetime <= MaximumFuture
        && IsBounded(ConnectionIdleTimeout);

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
