namespace KeyLoad;

/// <summary>Defines bounded execution time-series retention pages.</summary>
public static class SampleRetentionDefaults
{
    /// <summary>Gets the default number of expired records removed by one command.</summary>
    public const int DefaultDeletes = 128;
    /// <summary>Gets the maximum number of expired records removed by one command.</summary>
    public const int MaximumDeletes = 256;
}
