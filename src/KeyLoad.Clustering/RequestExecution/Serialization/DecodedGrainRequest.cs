namespace KeyLoad.Clustering;

internal readonly record struct DecodedGrainRequest(GrainRequestEnvelope Envelope, ReadOnlyMemory<byte> Payload);
