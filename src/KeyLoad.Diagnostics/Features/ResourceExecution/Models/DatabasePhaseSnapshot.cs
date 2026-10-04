using System.Collections.Immutable;

namespace KeyLoad.Diagnostics.Features.ResourceExecution;

/// <summary>A detached cumulative, non-atomic observation of completed scopes.</summary>
/// <param name="Enabled">Whether the physical bank was enabled.</param>
/// <param name="Frequency">The actual Stopwatch ticks per second, or zero when disabled.</param>
/// <param name="Started">The monotonic timestamp before capture, or zero when disabled.</param>
/// <param name="Finished">The monotonic timestamp after capture, or zero when disabled.</param>
/// <param name="Quality">Sticky quality observed through this capture.</param>
/// <param name="Histogram">Merged phase/outcome/bucket counters, or empty when disabled.</param>
/// <param name="BusyAttempts">Merged per-phase admission rejections, or empty when disabled.</param>
public sealed record DatabasePhaseSnapshot(
    bool Enabled,
    long Frequency,
    long Started,
    long Finished,
    DatabaseProfileQuality Quality,
    ImmutableArray<long> Histogram,
    ImmutableArray<long> BusyAttempts);
