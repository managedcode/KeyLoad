using Microsoft.Extensions.Options;

namespace KeyLoad.Orleans;

/// <summary>Centrally configured lifetimes for signed requests and their joined native execution streams.</summary>
[ConfigurationOptions]
public sealed class GrainRoutingOptions
{
    /// <summary>The centrally bound signed request execution section.</summary>
    public const string SectionName = "KeyLoad:GrainRouting";
    /// <summary>The startup failure for unsafe request and execution lifetimes.</summary>
    public const string ValidationMessage = "Grain request lifetimes must be positive, at most two minutes, and issuance must fit the accepted future window.";

    private const int DefaultLifetimeMinutes = 1;
    private const int MaximumFutureMinutes = 2;
    private const int MaximumProducerCount = 64;
    private const int MaximumFrameCount = 128;
    private const int MinimumPositiveCount = 1;
    private const int MaximumReplyBytesCeiling = 16_777_216;
    private const int MaximumInitialReplyBufferBytes = 4_096;
    private const int MaximumStartedBytesCeiling = 8_192;
    private const int MaximumFailedBytesCeiling = 65_536;
    private const int MaximumCompletedBytesCeiling = 16_842_752;
    private const int MaximumAggregateBytesCeiling = 16_850_944;
    private const int MaximumScratchBytesCeiling = 1_048_576;
    private const int MinimumScratchBytesCeiling = 256;
    private const int MaximumDetailCharactersCeiling = 4_096;
    private const int MaximumPrincipalBytesCeiling = 4_096;
    private const int MaximumContextBytesCeiling = 256;
    private static readonly TimeSpan MaximumDuration = TimeSpan.FromMinutes(MaximumFutureMinutes);

    /// <summary>Gets or sets the expiry interval stamped on newly signed requests.</summary>
    public TimeSpan RequestLifetime { get; set; } = TimeSpan.FromMinutes(DefaultLifetimeMinutes);
    /// <summary>Gets or sets the accepted future expiry window during request validation.</summary>
    public TimeSpan MaximumFuture { get; set; } = TimeSpan.FromMinutes(MaximumFutureMinutes);
    /// <summary>Gets or sets the cancellation lifetime shared by native stream producers and consumers.</summary>
    public TimeSpan ExecutionLifetime { get; set; } = TimeSpan.FromMinutes(DefaultLifetimeMinutes);
    /// <summary>Gets or sets the maximum active request stream producers admitted by one silo.</summary>
    public int MaximumRequestProducers { get; set; } = MaximumProducerCount;
    /// <summary>Gets or sets the maximum active request and capability frames admitted by one silo.</summary>
    public int MaximumTotalFrames { get; set; } = MaximumFrameCount;

    /// <summary>Gets or sets the encoded capability reply bytes.</summary>
    public int MaximumReplyBytes { get; set; } = MaximumReplyBytesCeiling;

    /// <summary>Gets or sets the initial encoded capability reply buffer reservation.</summary>
    public int InitialReplyBufferBytes { get; set; } = MaximumInitialReplyBufferBytes;

    /// <summary>Gets or sets the native started chunk bytes.</summary>
    public int MaximumStartedBytes { get; set; } = MaximumStartedBytesCeiling;

    /// <summary>Gets or sets the native failed chunk bytes.</summary>
    public int MaximumFailedBytes { get; set; } = MaximumFailedBytesCeiling;

    /// <summary>Gets or sets the native completed chunk bytes.</summary>
    public int MaximumCompletedBytes { get; set; } = MaximumCompletedBytesCeiling;

    /// <summary>Gets or sets the aggregate native stream bytes.</summary>
    public int MaximumAggregateBytes { get; set; } = MaximumAggregateBytesCeiling;

    /// <summary>Gets or sets the native serializer workspace bytes.</summary>
    public int MaximumScratchBytes { get; set; } = MaximumScratchBytesCeiling;

    /// <summary>Gets or sets the initial native serializer workspace bytes.</summary>
    public int MinimumScratchBytes { get; set; } = MinimumScratchBytesCeiling;

    /// <summary>Gets or sets the safe terminal error characters.</summary>
    public int MaximumDetailCharacters { get; set; } = MaximumDetailCharactersCeiling;

    /// <summary>Gets or sets the propagated native principal bytes.</summary>
    public int MaximumPrincipalBytes { get; set; } = MaximumPrincipalBytesCeiling;

    /// <summary>Gets or sets the propagated native request context bytes.</summary>
    public int MaximumContextBytes { get; set; } = MaximumContextBytesCeiling;

    /// <summary>Checks the bounded issuance and execution lifecycle before startup.</summary>
    /// <returns>Whether every configured interval preserves the accepted expiry bounds.</returns>
    public bool IsValid() => IsBounded(RequestLifetime) && IsBounded(MaximumFuture) && IsBounded(ExecutionLifetime)
        && RequestLifetime <= MaximumFuture
        && MaximumRequestProducers is >= MinimumPositiveCount and <= MaximumProducerCount
        && MaximumTotalFrames is >= MinimumPositiveCount and <= MaximumFrameCount
        && MaximumRequestProducers <= MaximumTotalFrames
        && MaximumReplyBytes is >= MinimumPositiveCount and <= MaximumReplyBytesCeiling
        && InitialReplyBufferBytes is >= MinimumPositiveCount and <= MaximumInitialReplyBufferBytes
        && MaximumStartedBytes is >= MinimumPositiveCount and <= MaximumStartedBytesCeiling
        && MaximumFailedBytes is >= MinimumPositiveCount and <= MaximumFailedBytesCeiling
        && MaximumCompletedBytes is >= MinimumPositiveCount and <= MaximumCompletedBytesCeiling
        && MaximumAggregateBytes is >= MinimumPositiveCount and <= MaximumAggregateBytesCeiling
        && MaximumScratchBytes is >= MinimumPositiveCount and <= MaximumScratchBytesCeiling
        && MinimumScratchBytes is >= MinimumPositiveCount and <= MinimumScratchBytesCeiling
        && MaximumDetailCharacters is >= MinimumPositiveCount and <= MaximumDetailCharactersCeiling
        && MaximumPrincipalBytes is >= MinimumPositiveCount and <= MaximumPrincipalBytesCeiling
        && MaximumContextBytes is >= MinimumPositiveCount and <= MaximumContextBytesCeiling
        && MinimumScratchBytes <= MaximumScratchBytes;

    /// <summary>Rejects an invalid captured routing snapshot before native execution.</summary>
    public void Validate()
    {
        if (!IsValid())
        { throw new OptionsValidationException(SectionName, typeof(GrainRoutingOptions), [ValidationMessage]); }
    }

    private static bool IsBounded(TimeSpan value) => value > TimeSpan.Zero && value <= MaximumDuration;
}
