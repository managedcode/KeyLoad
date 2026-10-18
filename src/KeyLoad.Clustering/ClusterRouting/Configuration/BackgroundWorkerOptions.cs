using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

/// <summary>Activation-local buffering policy, independent of Orleans worker activation count.</summary>
[ConfigurationOptions]
public sealed class BackgroundWorkerOptions
{
    /// <summary>Application configuration section.</summary>
    public const string SectionName = "KeyLoad:BackgroundWorkers";
    internal const string BufferFull = "The worker buffer is full; the persisted event remains available for retry.";
    private const string Invalid = "Worker intervals and event capacities must be positive.";
    private const int MinimumCapacity = 1;
    private const int DefaultFlushSeconds = 5;
    private const int DefaultCapacity = 256;
    /// <summary>Interval between completed native grain timer callbacks.</summary>
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(DefaultFlushSeconds);
    /// <summary>Maximum event hints retained by one worker activation.</summary>
    public int MaximumBufferedEvents { get; set; } = DefaultCapacity;

    /// <summary>Rejects invalid worker policy before admitting events.</summary>
    public void Validate()
    {
        if (FlushInterval <= TimeSpan.Zero || MaximumBufferedEvents < MinimumCapacity)
        {
            throw new OptionsValidationException(SectionName, typeof(BackgroundWorkerOptions), [Invalid]);
        }
    }
}
