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
    private const int MaximumFrameCount = 128;
    private const int MaximumReplyBytesCeiling = 16_777_216;
    private const int MaximumStartedBytesCeiling = 8_192;
    private const int MaximumFailedBytesCeiling = 65_536;
    private const int MaximumCompletedBytesCeiling = 16_842_752;
    private const int MaximumAggregateBytesCeiling = 16_850_944;
    private const int MaximumScratchBytesCeiling = 1_048_576;
    private const int MinimumScratchBytesCeiling = 256;
    private const int MaximumDetailCharactersCeiling = 4_096;
    private const int MaximumPrincipalBytesCeiling = 4_096;
    private const int MaximumContextBytesCeiling = 256;
    private static readonly TimeSpan MaximumDuration = TimeSpan.Parse(MaximumExecutionLifetime, System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Gets or sets the expiry interval stamped on newly issued envelopes.</summary>
    public TimeSpan RequestLifetime { get; set; } = TimeSpan.FromMinutes(1);
    /// <summary>Gets or sets the accepted future expiry window during request validation.</summary>
    public TimeSpan MaximumFuture { get; set; } = MaximumDuration;
    /// <summary>Gets or sets the cancellation lifetime shared by execution stream producers and consumers.</summary>
    public TimeSpan ExecutionLifetime { get; set; } = TimeSpan.FromMinutes(1);
    /// <summary>Gets or sets the frames one long-running response stream can carry, including progress and its terminal frame.</summary>
    public int MaximumStreamFrames { get; set; } = MaximumFrameCount;

    /// <summary>Gets or sets idle physical transport expiry while no operation is active.</summary>
    public TimeSpan ConnectionIdleTimeout { get; set; } = MaximumDuration;

    /// <summary>Gets or sets the encoded capability reply bytes.</summary>
    public int MaximumReplyBytes { get; set; } = MaximumReplyBytesCeiling;

    /// <summary>Gets or sets the execution started chunk bytes.</summary>
    public int MaximumStartedBytes { get; set; } = MaximumStartedBytesCeiling;

    /// <summary>Gets or sets the execution failed chunk bytes.</summary>
    public int MaximumFailedBytes { get; set; } = MaximumFailedBytesCeiling;

    /// <summary>Gets or sets the execution completed chunk bytes.</summary>
    public int MaximumCompletedBytes { get; set; } = MaximumCompletedBytesCeiling;

    /// <summary>Gets or sets the aggregate execution stream bytes.</summary>
    public int MaximumAggregateBytes { get; set; } = MaximumAggregateBytesCeiling;

    /// <summary>Gets or sets the execution serializer workspace bytes.</summary>
    public int MaximumScratchBytes { get; set; } = MaximumScratchBytesCeiling;

    /// <summary>Gets or sets the initial execution serializer workspace bytes.</summary>
    public int MinimumScratchBytes { get; set; } = MinimumScratchBytesCeiling;

    /// <summary>Gets or sets the safe terminal error characters.</summary>
    public int MaximumDetailCharacters { get; set; } = MaximumDetailCharactersCeiling;

    /// <summary>Gets or sets the propagated execution principal bytes.</summary>
    public int MaximumPrincipalBytes { get; set; } = MaximumPrincipalBytesCeiling;

    /// <summary>Gets or sets the propagated execution request context bytes.</summary>
    public int MaximumContextBytes { get; set; } = MaximumContextBytesCeiling;

    /// <summary>Checks the bounded issuance and execution lifecycle before startup.</summary>
    /// <returns>Whether every configured interval preserves the accepted expiry bounds.</returns>
    public bool IsValid() => IsBounded(RequestLifetime) && IsBounded(MaximumFuture) && IsBounded(ExecutionLifetime)
        && RequestLifetime <= MaximumFuture
        && MaximumStreamFrames is >= 1 and <= MaximumFrameCount
        && IsBounded(ConnectionIdleTimeout)
        && MaximumReplyBytes is >= 1 and <= MaximumReplyBytesCeiling
        && MaximumStartedBytes is >= 1 and <= MaximumStartedBytesCeiling
        && MaximumFailedBytes is >= 1 and <= MaximumFailedBytesCeiling
        && MaximumCompletedBytes is >= 1 and <= MaximumCompletedBytesCeiling
        && MaximumAggregateBytes is >= 1 and <= MaximumAggregateBytesCeiling
        && MaximumScratchBytes is >= 1 and <= MaximumScratchBytesCeiling
        && MinimumScratchBytes is >= 1 and <= MinimumScratchBytesCeiling
        && MaximumDetailCharacters is >= 1 and <= MaximumDetailCharactersCeiling
        && MaximumPrincipalBytes is >= 1 and <= MaximumPrincipalBytesCeiling
        && MaximumContextBytes is >= 1 and <= MaximumContextBytesCeiling
        && MinimumScratchBytes <= MaximumScratchBytes;

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
