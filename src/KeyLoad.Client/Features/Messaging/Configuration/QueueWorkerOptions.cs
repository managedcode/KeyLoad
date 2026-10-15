using Microsoft.Extensions.Options;

namespace KeyLoad.Client;

/// <summary>Bounds one caller-owned queue worker invocation without changing server policy.</summary>
[ConfigurationOptions]
public sealed record QueueWorkerOptions
{
    /// <summary>The native caller composition section.</summary>
    public const string SectionName = "KeyLoad:QueueWorker";
    /// <summary>Safe failure before any worker operation.</summary>
    public const string ValidationMessage = "Queue worker budgets must be positive and bounded; renewal must precede the requested lease.";
    private const int PositiveBudgetBoundary = 0;
    private const int NoUncertaintyRetries = 0;
    private const int DefaultHandlers = 4;
    private const int HandlerCeiling = 100;
    private const int DefaultDeliveries = 64;
    private const int InvocationCountCeiling = 4096;
    private const int DefaultCalls = 128;
    private const int DefaultBytes = 1_048_576;
    private const int ByteCeiling = 16_777_216;
    private const int DefaultLeaseSeconds = 30;
    private const int LeaseCeilingSeconds = 300;
    private const int DefaultRenewalSeconds = 10;
    private const int DefaultPollMilliseconds = 100;
    private const int MaximumPollSeconds = 1;
    private const int MaximumUncertaintyRetries = 1;

    /// <summary>Maximum simultaneously owned handler tasks.</summary>
    public int MaximumConcurrentHandlers { get; init; } = DefaultHandlers;
    /// <summary>Maximum deliveries admitted by this finite invocation.</summary>
    public int MaximumDeliveries { get; init; } = DefaultDeliveries;
    /// <summary>Maximum distinct receive operations, including empty polls.</summary>
    public int MaximumReceiveCalls { get; init; } = DefaultCalls;
    /// <summary>Maximum distinct renewal operations in the single renewal owner.</summary>
    public int MaximumRenewalCalls { get; init; } = DefaultCalls;
    /// <summary>Retained active UTF8 payload and header bytes; this is not measured CLR RAM.</summary>
    public int MaximumInFlightBytes { get; init; } = DefaultBytes;
    /// <summary>Additional native receive byte request ceiling.</summary>
    public int MaximumReceiveBytes { get; init; } = DefaultBytes;
    /// <summary>Requested native lease duration; stricter resource policies may refuse.</summary>
    public int LeaseSeconds { get; init; } = DefaultLeaseSeconds;
    /// <summary>Cadence of the single bounded renewal owner.</summary>
    public TimeSpan RenewalInterval { get; init; } = TimeSpan.FromSeconds(DefaultRenewalSeconds);
    /// <summary>Delay after a genuine empty receive; no server long-poll is implied.</summary>
    public TimeSpan EmptyPollDelay { get; init; } = TimeSpan.FromMilliseconds(DefaultPollMilliseconds);
    /// <summary>Exact same-ID attempts allowed after an actual unknown write.</summary>
    public int UncertaintyRetries { get; init; } = MaximumUncertaintyRetries;

    /// <summary>Checks count, byte and renewal inequalities before native IO.</summary>
    public bool IsValid() => MaximumConcurrentHandlers is > PositiveBudgetBoundary and <= HandlerCeiling
        && MaximumDeliveries is > PositiveBudgetBoundary and <= InvocationCountCeiling
        && MaximumReceiveCalls is > PositiveBudgetBoundary and <= InvocationCountCeiling
        && MaximumRenewalCalls is > PositiveBudgetBoundary and <= InvocationCountCeiling
        && MaximumInFlightBytes is > PositiveBudgetBoundary and <= ByteCeiling
        && MaximumReceiveBytes is > PositiveBudgetBoundary and <= ByteCeiling
        && MaximumReceiveBytes <= MaximumInFlightBytes
        && LeaseSeconds is > PositiveBudgetBoundary and <= LeaseCeilingSeconds
        && RenewalInterval > TimeSpan.Zero && RenewalInterval < TimeSpan.FromSeconds(LeaseSeconds)
        && EmptyPollDelay > TimeSpan.Zero && EmptyPollDelay <= TimeSpan.FromSeconds(MaximumPollSeconds)
        && UncertaintyRetries is >= NoUncertaintyRetries and <= MaximumUncertaintyRetries;

    /// <summary>Rejects invalid invocation configuration before native IO.</summary>
    public void Validate()
    {
        if (!IsValid())
        {
            throw new OptionsValidationException(SectionName, typeof(QueueWorkerOptions), [ValidationMessage]);
        }
    }
}
