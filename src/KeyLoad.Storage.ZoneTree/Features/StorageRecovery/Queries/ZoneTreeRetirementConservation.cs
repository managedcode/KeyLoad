namespace KeyLoad.Storage.ZoneTree;

/// <summary>Verifies a bounded original source view before any fresh archive identity rewrite.</summary>
internal static class ZoneTreeRetirementConservation
{
    private const string Exhausted = "The native retirement source proof exceeds its original range budget.";

    internal static ZoneTreeRetirementConservationProof Capture(IKeyValueView view,
        ZoneTreeStoreOptions options, TimeProvider clock,
        ResourceRetirementConservationSourceOriginV1? admittedOrigin = null, CancellationToken token = default)
    {
        var capture = new ZoneTreeCheckpointSourceProof(view, options, clock, token, admittedOrigin);
        var prefixes = ResourceRetirementAuthorityClassifier.Prefixes
            .Append(ResourceRetirementConservationKeys.StepPrefix())
            .Append(ResourceRetirementConservationKeys.ReleasePrefix());
        foreach (var prefix in prefixes)
        {
            var page = capture.View.VisitRange(prefix, options.MaximumReadCutRecords, (key, value) =>
            {
                capture.Observe(key, value);
                return true;
            }, cancellationToken: token);
            if (page.HasMore)
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, Exhausted);
            }
        }
        return capture.Complete();
    }
}
