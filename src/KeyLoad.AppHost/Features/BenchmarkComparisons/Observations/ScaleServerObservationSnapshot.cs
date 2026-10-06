namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal sealed record ScaleServerObservationSnapshot(ScaleServerHardware? Hardware,
    ScaleServerEnvelope? AppHostEnvelope, ScaleServerContainer[] Containers, string[] MissingEvidence,
    bool Qualified, ScaleServerObservationPolicy ObservationPolicy);
