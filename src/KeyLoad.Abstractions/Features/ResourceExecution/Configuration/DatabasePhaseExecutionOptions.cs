using Microsoft.Extensions.Options;

namespace KeyLoad;

/// <summary>Immutable process-start policy for the bounded native phase counter bank.</summary>
[ConfigurationOptions]
public sealed record DatabasePhaseExecutionOptions
{
    /// <summary>The centrally bound configuration identity.</summary>
    public const string SectionName = "KeyLoad:DatabasePhaseExecution";
    /// <summary>The validation failure detail.</summary>
    public const string ValidationMessage = "The database phase execution settings are invalid.";
    private const int SingleStripe = 1;
    private const int TwoStripes = 2;
    private const int DefaultStripeCount = 4;
    private const int MinimumCasAttempts = 1;
    private const int DefaultMaximumCasAttempts = 4;

    /// <summary>Whether this physical process records optional phase counters.</summary>
    public bool Enabled { get; init; }
    /// <summary>The bounded power-of-two reservation selected before allocation.</summary>
    public int StripeCount { get; init; } = DefaultStripeCount;
    /// <summary>The maximum compare-exchange attempts for each recorded counter.</summary>
    public int MaximumCasAttempts { get; init; } = DefaultMaximumCasAttempts;

    /// <summary>Whether settings preserve the existing qualified memory and work ceilings.</summary>
    public bool IsValid() => StripeCount is SingleStripe or TwoStripes or DefaultStripeCount
        && MaximumCasAttempts is >= MinimumCasAttempts and <= DefaultMaximumCasAttempts;

    /// <summary>Rejects malformed settings before physical resource ownership.</summary>
    public void Validate()
    {
        if (!IsValid())
        {
            throw new OptionsValidationException(Options.DefaultName, typeof(DatabasePhaseExecutionOptions), [ValidationMessage]);
        }
    }
}
