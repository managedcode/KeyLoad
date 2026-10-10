using KeyLoad.Core;

namespace KeyLoad.Orleans;

internal static class GrainRequestPayloadVerification
{
    internal static DecodedGrainRequest Verify(GrainRequestCodec originalCodec, DatabaseEngine database,
        int maximumTokenCharacters, string signedRequest)
    {
        if (string.IsNullOrEmpty(signedRequest) || signedRequest.Length > maximumTokenCharacters)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }

        var request = database.Verify<GrainRequestEnvelope>(signedRequest, maximumTokenCharacters);
        originalCodec.ValidateScope(request);
        if (request.Payload.Length > database.Limits.MaxBatchBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, GrainRoutingProtocol.InvalidRequest);
        }

        GrainNativePayload.Validate(request.Payload.Span);
        return new(request, request.Payload);
    }
}
