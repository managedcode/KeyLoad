using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

/// <summary>Activation-local buffering policy, independent of Orleans worker activation count.</summary>
[ConfigurationOptions]
public sealed class BackgroundWorkerOptions
{
    /// <summary>Application configuration section.</summary>
    public const string SectionName = "KeyLoad:BackgroundWorkers";
    private const string Invalid = "Worker sweep intervals must be positive.";
    private const int DefaultFlushSeconds = 5;
    /// <summary>Interval between completed execution grain timer callbacks.</summary>
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(DefaultFlushSeconds);
    /// <summary>Maximum event hints retained by one worker activation.</summary>

    /// <summary>Rejects invalid worker policy before admitting events.</summary>
    public void Validate()
    {
        if (FlushInterval <= TimeSpan.Zero)
        {
            throw new OptionsValidationException(SectionName, typeof(BackgroundWorkerOptions), [Invalid]);
        }
    }
}
