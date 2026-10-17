namespace KeyLoad.Clustering;

internal sealed record NativeResourceWorkSnapshot(Guid RequestId, NativeRequestWorkKind Kind,
    long AdmissionGeneration, long LastPublicationGeneration);
