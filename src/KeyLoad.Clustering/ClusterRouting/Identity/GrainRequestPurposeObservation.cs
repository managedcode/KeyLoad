namespace KeyLoad.Clustering;

internal static class GrainRequestPurposeObservation
{
    internal static GrainRequestEnvelope BeforeSign(IGrainRequestPurposeObserver? observer,
        GrainRequestEnvelope original)
    {
        if (observer is null)
        {
            return original;
        }
        var purpose = observer.BeforeSign(original);
        return purpose is null ? original : original with
        {
            Purpose = purpose
        };
    }

    internal static void Validate(GrainRequestEnvelope actual, GrainRequestScopeCut cut,
        IGrainRequestPurposeObserver? observer)
    {
        if (observer is null)
        {
            GrainRequestScope.Validate(actual, cut.Incarnation, cut.EvaluatedAt, cut.MaximumFuture);
            return;
        }
        try
        {
            GrainRequestScope.Validate(actual, cut.Incarnation, cut.EvaluatedAt, cut.MaximumFuture);
        }
        catch (KeyLoadException failure)
        {
            try
            {
                observer.RejectedAfterMac(actual, failure, cut);
            }
            catch (Exception observation) when (NativeCqrsBoundaryErrors.IsNonFatal(observation))
            {
                throw new AggregateException(failure, observation);
            }
            catch (Exception observation) when (!NativeCqrsBoundaryErrors.IsNonFatal(observation))
            {
                throw new AggregateException(failure, observation);
            }
            throw;
        }
    }
}
