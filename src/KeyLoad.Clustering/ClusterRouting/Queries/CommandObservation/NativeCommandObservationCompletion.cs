namespace KeyLoad.Clustering;

/// <summary>Allows counted strict refusals only after the same original native settlement has no cleanup failure.</summary>
internal static class NativeCommandObservationCompletion
{
    internal static async Task<Exception?> CompleteAsync(NativeCommandObservation original,
        Exception? primary, Exception? cleanup, KeyLoadException? refusal)
    {
        Exception? completion = null;
        try
        {
            await original.CompleteAsync(primary, cleanup).ConfigureAwait(true);
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            completion = error;
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            completion = error;
        }
        if (refusal is not null && ReferenceEquals(primary, refusal) && cleanup is null
            && (completion is null || ReferenceEquals(completion, refusal))
            && original.Lease.Settlement.IsCompletedSuccessfully)
        {
            var actual = await original.Lease.Settlement.ConfigureAwait(true);
            if (ReferenceEquals(actual.OriginalFailure, refusal) && actual.CleanupFailures.IsEmpty)
            {
                return null;
            }
        }
        return NativeRequestWorkSettlement.Preserve(NativeRequestWorkSettlement.Preserve(primary, cleanup), completion);
    }

    // Collect exact independent failure references before the retained original settlement selects fatal precedence.
    internal static Exception? Retain(Exception? original, Exception additional)
    {
        if (original is null || ReferenceEquals(original, additional))
        {
            return additional;
        }
        return new AggregateException(original, additional);
    }
}
