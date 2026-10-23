namespace KeyLoad.Clustering;

internal static class GrainRequestPayloadVerification
{
    internal static DecodedGrainRequest DecodeRead(GrainRequestCodec codec, GrainRequestEnvelope request, Guid actorId)
    {
        var decoded = codec.DecodeRequest(request, actorId);
        if (decoded.Envelope.ReadKind is null)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }

        return decoded;
    }

    internal static DecodedGrainRequest VerifyRequest(DecodedGrainRequest request, Guid actorId)
    {
        GrainRequestScope.RequireRequestIdentity(request.Envelope, actorId);

        return request;
    }

    internal static DecodedGrainRequest Decode(GrainRequestCodec codec, GrainRequestEnvelope request)
    {
        if (request is null)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }

        codec.ValidateVerifiedScope(request);
        if (request.Payload.Length > codec.Limits.MaxBatchBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, GrainRoutingProtocol.InvalidRequest);
        }

        GrainPayload.Validate(request.Payload.Span);
        return new(request, request.Payload);
    }
}
