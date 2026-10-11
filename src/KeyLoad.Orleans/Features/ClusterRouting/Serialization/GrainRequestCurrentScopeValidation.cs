using KeyLoad.Core;

namespace KeyLoad.Orleans;

internal static class GrainRequestCurrentScopeValidation
{
    internal static void Validate(GrainRequestEnvelope request, DatabaseEngine database,
        TimeProvider clock, GrainRoutingOptions settings, IGrainRequestPurposeObserver? observer)
    {
        var cut = new GrainRequestScopeCut(database.Store.Identity.Incarnation,
            clock.GetUtcNow(), settings.MaximumFuture);
        GrainRequestPurposeObservation.Validate(request, cut, observer);
    }
}
