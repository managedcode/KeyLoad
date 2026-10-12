using KeyLoad.Core;

namespace KeyLoad.Orleans;

internal static class GrainRequestIssuance
{
    internal static string Issue(GrainRequestCodec codec, DatabaseEngine database,
        IGrainRequestPurposeObserver? observer, GrainRequestEnvelope request)
    {
        codec.ValidateScope(request);
        return database.Sign(GrainRequestPurposeObservation.BeforeSign(observer, request));
    }
}
