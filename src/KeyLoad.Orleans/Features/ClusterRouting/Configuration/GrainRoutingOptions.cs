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

    /// <summary>Checks the bounded issuance and execution lifecycle before startup.</summary>
    /// <returns>Whether every configured interval preserves the accepted expiry bounds.</returns>
    public bool IsValid() => IsBounded(RequestLifetime) && IsBounded(MaximumFuture) && IsBounded(ExecutionLifetime)
        && RequestLifetime <= MaximumFuture
        && MaximumRequestProducers is >= MinimumPositiveCount and <= MaximumProducerCount
        && MaximumTotalFrames is >= MinimumPositiveCount and <= MaximumFrameCount
        && MaximumRequestProducers <= MaximumTotalFrames;

    private static bool IsBounded(TimeSpan value) => value > TimeSpan.Zero && value <= MaximumDuration;
}
