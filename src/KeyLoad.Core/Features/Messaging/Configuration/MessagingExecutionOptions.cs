namespace KeyLoad.Core;

/// <summary>Node-owned topic publication, recurring work and retry computation admission.</summary>
[ConfigurationOptions]
public sealed record MessagingExecutionOptions
{
    /// <summary>The centrally bound messaging execution section.</summary>
    public const string SectionName = "KeyLoad:MessagingExecution";
    /// <summary>Safe rejection raised before messaging execution is admitted.</summary>
    public const string ValidationMessage = "Messaging work limits must be positive and the retry exponent must remain within its arithmetic bound.";

    private const int DefaultMaximumTopicEvents = 256;
    private const int DefaultMaximumOccurrenceCatchUp = 32;
    private const int DefaultMaximumRetryExponent = 20;
    private const int MinimumWorkCount = 1;
    private const int MinimumRetryExponent = 0;
    private const int MaximumSafeRetryExponent = 20;

    /// <summary>Gets the maximum events in one authorized topic publication.</summary>
    public int MaximumTopicEvents { get; init; } = DefaultMaximumTopicEvents;
    /// <summary>Gets the maximum recurring occurrences emitted in one atomic catch-up operation.</summary>
    public int MaximumOccurrenceCatchUp { get; init; } = DefaultMaximumOccurrenceCatchUp;
    /// <summary>Gets the capped retry exponent before applying the persisted queue's maximum delay.</summary>
    public int MaximumRetryExponent { get; init; } = DefaultMaximumRetryExponent;

    /// <summary>Checks independent work budgets and the existing retry arithmetic safety ceiling.</summary>
    public bool IsValid() => MaximumTopicEvents >= MinimumWorkCount
        && MaximumOccurrenceCatchUp >= MinimumWorkCount
        && MaximumRetryExponent >= MinimumRetryExponent && MaximumRetryExponent <= MaximumSafeRetryExponent;

    /// <summary>Rejects invalid settings before database recovery or command admission.</summary>
    public void Validate()
    {
        if (!IsValid())
        { throw new InvalidOperationException(ValidationMessage); }
    }
}
