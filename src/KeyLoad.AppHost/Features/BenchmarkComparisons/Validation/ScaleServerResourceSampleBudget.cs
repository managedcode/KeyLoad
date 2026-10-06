using Microsoft.Extensions.Options;

namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal sealed class ScaleServerResourceSampleBudget

{
    private readonly int maximum;
    private int _used;
    internal TimeProvider TimeProvider { get; }
    internal ScaleServerResourceOptions Settings { get; }
    internal BenchmarkProvenanceOptions Provenance { get; }

    internal ScaleServerResourceSampleBudget(IOptions<ScaleServerResourceOptions> settings,
        IOptions<BenchmarkProvenanceOptions> provenance, int? maximumBytes = null, TimeProvider? timeProvider = null)
    {
        TimeProvider = timeProvider ?? TimeProvider.System;
        Settings = settings.Value;
        Settings.Validate();
        Provenance = provenance.Value;
        maximum = maximumBytes ?? Settings.MaxSampleMetadataBytes;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximum);
        if (maximum > Settings.MaxSampleMetadataBytes)
        { throw new ArgumentOutOfRangeException(nameof(maximumBytes)); }
    }
    internal int Remaining => maximum - _used;

    internal void Charge(int bytes)
    {
        const string MessageText = "Server resource sample exceeded its bound.";

        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        if (bytes > Remaining)
        {
            throw new InvalidDataException(MessageText);
        }

        _used += bytes;
    }
}
