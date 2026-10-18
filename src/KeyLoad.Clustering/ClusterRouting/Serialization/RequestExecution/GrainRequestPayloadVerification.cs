namespace KeyLoad.Clustering;

internal static class GrainRequestPayloadVerification
{
    internal static DecodedGrainRequest VerifyRead(GrainRequestCodec codec, string signedRequest, Guid actorId)
    {
        var request = codec.VerifyRequest(signedRequest, actorId);
        if (request.Envelope.ReadKind is null)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }

        return request;
    }

    internal static DecodedGrainRequest VerifyRequest(DecodedGrainRequest request, Guid actorId)
    {
        GrainRequestScope.RequireRequestIdentity(request.Envelope, actorId);

        return request;
    }

    internal static DecodedGrainRequest Verify(GrainRequestCodec originalCodec,
        int maximumTokenCharacters, string signedRequest)
    {
        if (string.IsNullOrEmpty(signedRequest) || signedRequest.Length > maximumTokenCharacters)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }

        var request = originalCodec.VerifySigned<GrainRequestEnvelope>(signedRequest, maximumTokenCharacters);
        originalCodec.ValidateVerifiedScope(request);
        if (request.Payload.Length > originalCodec.Limits.MaxBatchBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, GrainRoutingProtocol.InvalidRequest);
        }

        GrainNativePayload.Validate(request.Payload.Span);
        return new(request, request.Payload);
    }
}
