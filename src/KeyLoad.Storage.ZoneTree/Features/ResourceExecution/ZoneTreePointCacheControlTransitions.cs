namespace KeyLoad.Storage.ZoneTree.Features.ResourceExecution;

/// <summary>Performs cold replacement and exact-receipt continuity under the caller's writer gate.</summary>
internal static class ZoneTreePointCacheControlTransitions
{
    internal static ZoneTreePointCacheControlResult ApplyUnderWrite(
        ZoneTreePointCacheControlState state, CacheReadPermitAcceptance receipt)
    {
        if (Volatile.Read(ref state.Closed))
        {
            return ZoneTreePointCacheControlResult.Closed;
        }

        if (!IsEligible(state, receipt))
        {
            return ZoneTreePointCacheControlResult.Rejected;
        }

        var transition = BeginApply(state, receipt);
        if (transition.Result is { } result)
        {
            return result;
        }

        transition.OldCache?.Dispose();
        ClearDisposedCache(state, transition.OldCache);

        return CreateAndPublish(state, receipt, transition.ExpectedBinding);
    }

    private static ApplyTransition BeginApply(ZoneTreePointCacheControlState state,
        CacheReadPermitAcceptance receipt)
    {
        lock (state.TransitionGate)
        {
            if (state.Closed)
            {
                return ApplyTransition.Completed(ZoneTreePointCacheControlResult.Closed);
            }

            if (!IsEligible(state, receipt))
            {
                return ApplyTransition.Completed(ZoneTreePointCacheControlResult.Rejected);
            }

            var previous = state.Binding;
            if (IsSameReadyBinding(previous, receipt))
            {
                var result = IsEligible(state, receipt)
                    ? ZoneTreePointCacheControlResult.AlreadyApplied
                    : ZoneTreePointCacheControlResult.Rejected;
                return ApplyTransition.Completed(result);
            }

            if (CanReuse(previous, receipt))
            {
                var renewed = new ZoneTreePointCacheBinding(receipt, previous!.Cache, false);
                if (!IsEligible(state, receipt))
                {
                    return ApplyTransition.Completed(ZoneTreePointCacheControlResult.Rejected);
                }

                Volatile.Write(ref state.Binding, renewed);
                return ApplyTransition.Completed(ZoneTreePointCacheControlResult.Applied);
            }

            var oldCache = state.MaintenanceCacheValue;
            var detached = previous is null
                ? null
                : new ZoneTreePointCacheBinding(previous.Acceptance, previous.Cache, true);
            Volatile.Write(ref state.Binding, detached);
            return new ApplyTransition(null, oldCache, detached);
        }
    }

    private static void ClearDisposedCache(ZoneTreePointCacheControlState state,
        ZoneTreePointCache? oldCache)
    {
        if (oldCache is null)
        {
            return;
        }

        lock (state.TransitionGate)
        {
            if (ReferenceEquals(state.MaintenanceCacheValue, oldCache))
            {
                Volatile.Write(ref state.MaintenanceCacheValue, null);
            }
        }
    }

    private static ZoneTreePointCacheControlResult CreateAndPublish(ZoneTreePointCacheControlState state,
        CacheReadPermitAcceptance receipt, ZoneTreePointCacheBinding? expectedBinding)
    {
        if (Volatile.Read(ref state.Closed))
        {
            return ZoneTreePointCacheControlResult.Closed;
        }

        if (!IsEligible(state, receipt))
        {
            return ZoneTreePointCacheControlResult.Rejected;
        }

        ZoneTreePointCache? candidate = null;
        try
        {
            candidate = new ZoneTreePointCache(state.Options);
            var result = PublishCandidate(state, candidate, receipt, expectedBinding, out var published);
            if (published)
            {
                candidate = null;
            }

            return result;
        }
        finally
        {
            candidate?.Dispose();
        }
    }

    private static ZoneTreePointCacheControlResult PublishCandidate(ZoneTreePointCacheControlState state,
        ZoneTreePointCache candidate, CacheReadPermitAcceptance receipt,
        ZoneTreePointCacheBinding? expectedBinding, out bool published)
    {
        published = false;
        lock (state.TransitionGate)
        {
            var result = candidate.Snapshot().Enabled
                ? ZoneTreePointCacheControlResult.Applied
                : ZoneTreePointCacheControlResult.Unavailable;
            var binding = new ZoneTreePointCacheBinding(receipt, candidate, false);
            if (state.Closed)
            {
                return ZoneTreePointCacheControlResult.Closed;
            }

            if (!IsEligible(state, receipt))
            {
                return ZoneTreePointCacheControlResult.Rejected;
            }

            if (!ReferenceEquals(expectedBinding, state.Binding))
            {
                return ZoneTreePointCacheControlResult.Busy;
            }

            Volatile.Write(ref state.MaintenanceCacheValue, candidate);
            Volatile.Write(ref state.Binding, binding);
            published = true;
            return result;
        }
    }

    private static bool IsEligible(ZoneTreePointCacheControlState state,
        CacheReadPermitAcceptance receipt)
    {
        return receipt.Revision > 0 && state.Permit.IsCurrentAcceptance(receipt);
    }

    private static bool IsSameReadyBinding(ZoneTreePointCacheBinding? binding,
        CacheReadPermitAcceptance receipt)
    {
        return binding is { Retired: false } && binding.Acceptance == receipt
            && binding.Cache.Snapshot().Enabled;
    }

    private static bool CanReuse(ZoneTreePointCacheBinding? binding,
        CacheReadPermitAcceptance receipt)
    {
        return binding is { Retired: false } && receipt.Continuous
            && binding.Acceptance.Revision == receipt.PreviousRevision
            && binding.Cache.Snapshot().Enabled;
    }

    private sealed record ApplyTransition(ZoneTreePointCacheControlResult? Result,
        ZoneTreePointCache? OldCache, ZoneTreePointCacheBinding? ExpectedBinding)
    {
        internal static ApplyTransition Completed(ZoneTreePointCacheControlResult result)
        {
            return new ApplyTransition(result, null, null);
        }
    }
}
