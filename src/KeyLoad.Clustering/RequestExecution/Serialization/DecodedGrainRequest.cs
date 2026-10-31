namespace KeyLoad.Clustering;

internal readonly record struct DecodedGrainRequest(GrainRequestEnvelope Envelope, object Payload);
