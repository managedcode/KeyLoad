using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Centrally validated operational limits for the private request lifecycle probe.</summary>
[ConfigurationOptions]
internal sealed class RequestProbeExecutionOptions
{
    internal const string SectionName = "KeyLoad:RequestProbeExecution";
    internal const string ValidationMessage = "Private request probe durations and active gates exceed their accepted bounds.";
    private const int MaximumHoldSeconds = 60;
    private const int DefaultPollMilliseconds = 100;
    private const int MaximumPollSeconds = 1;
    private const int MaximumPermittedGates = 4;
    private const int MinimumPositiveCount = 1;
    private const int MaximumFileCeiling = 400;
    private const int MaximumArmCeiling = 32;
    private const int MaximumMarkerCeiling = 8;
    private const int MaximumAggregateByteCeiling = 1_048_576;
    private const int MaximumRecordByteCeiling = 8_192;
    private const int MaximumPrincipalByteCeiling = 256;
    private const int MaximumJsonDepthCeiling = 4;
    private const int MaximumFileBufferByteCeiling = 4_096;
    private const int OverflowProbeBytes = 1;
    private static readonly TimeSpan MaximumHoldTimeout = TimeSpan.FromSeconds(MaximumHoldSeconds);
    private static readonly TimeSpan MaximumPollInterval = TimeSpan.FromSeconds(MaximumPollSeconds);

    /// <summary>Gets or sets the finite cancellation ceiling for one private probe hold.</summary>
    public TimeSpan HoldTimeout { get; set; } = TimeSpan.FromSeconds(MaximumHoldSeconds);
    /// <summary>Gets or sets the interval between private arm release checks.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(DefaultPollMilliseconds);
    /// <summary>Gets or sets the maximum simultaneously held private probe gates.</summary>
    public int MaximumActiveGates { get; set; } = MaximumPermittedGates;

    /// <summary>Gets or sets the bounded number of retained private probe files.</summary>
    public int MaximumFiles { get; set; } = MaximumFileCeiling;
    /// <summary>Gets or sets the number of retained arm identities.</summary>
    public int MaximumArms { get; set; } = MaximumArmCeiling;
    /// <summary>Gets or sets the retained markers for one request.</summary>
    public int MaximumMarkersPerRequest { get; set; } = MaximumMarkerCeiling;
    /// <summary>Gets or sets the aggregate retained evidence bytes.</summary>
    public int MaximumAggregateBytes { get; set; } = MaximumAggregateByteCeiling;
    /// <summary>Gets or sets one encoded private evidence record's byte bound.</summary>
    public int MaximumRecordBytes { get; set; } = MaximumRecordByteCeiling;
    /// <summary>Gets or sets the bounded principal identity bytes.</summary>
    public int MaximumPrincipalBytes { get; set; } = MaximumPrincipalByteCeiling;
    /// <summary>Gets or sets the private evidence parser depth.</summary>
    public int MaximumJsonDepth { get; set; } = MaximumJsonDepthCeiling;
    /// <summary>Gets or sets the native file writer workspace.</summary>
    public int FileBufferBytes { get; set; } = MaximumFileBufferByteCeiling;
    internal int ReadBufferBytes => checked(MaximumRecordBytes + OverflowProbeBytes);

    internal bool IsValid() => HoldTimeout > TimeSpan.Zero && HoldTimeout <= MaximumHoldTimeout
        && PollInterval > TimeSpan.Zero && PollInterval <= MaximumPollInterval
        && PollInterval < HoldTimeout
        && MaximumActiveGates is >= MinimumPositiveCount and <= MaximumPermittedGates
        && MaximumFiles is >= MinimumPositiveCount and <= MaximumFileCeiling
        && MaximumArms is >= MinimumPositiveCount and <= MaximumArmCeiling
        && MaximumMarkersPerRequest is >= MinimumPositiveCount and <= MaximumMarkerCeiling
        && MaximumAggregateBytes is >= MinimumPositiveCount and <= MaximumAggregateByteCeiling
        && MaximumRecordBytes is >= MinimumPositiveCount and <= MaximumRecordByteCeiling
        && MaximumPrincipalBytes is >= MinimumPositiveCount and <= MaximumPrincipalByteCeiling
        && MaximumJsonDepth is >= MinimumPositiveCount and <= MaximumJsonDepthCeiling
        && FileBufferBytes is >= MinimumPositiveCount and <= MaximumFileBufferByteCeiling
        && MaximumRecordBytes <= MaximumAggregateBytes;

    internal void Validate()
    {
        if (!IsValid())
        { throw new OptionsValidationException(SectionName, typeof(RequestProbeExecutionOptions), [ValidationMessage]); }
    }
}
