namespace KeyLoad.Clustering;

internal readonly record struct DecodedGrainRequest(GrainRequestEnvelope Envelope, object /* KLD0041: Retain the verified current envelope payload reference for closed-kind typed decoding without changing its native wire representation. */ Payload);
