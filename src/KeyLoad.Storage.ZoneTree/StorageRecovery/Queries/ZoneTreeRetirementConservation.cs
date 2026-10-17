namespace KeyLoad.Storage.ZoneTree;

/// <summary>Verifies a bounded original source view before any fresh archive identity rewrite.</summary>
internal static class ZoneTreeRetirementConservation
{
    private const int EmptyPage = 0;

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
            CapturePrefix(capture, prefix, capture.View.MaximumRangeRecords, token);
        }
        return capture.Complete();
    }

    private static void CapturePrefix(ZoneTreeCheckpointSourceProof capture, byte[] prefix,
        int maximumRecords, CancellationToken token)
    {
        byte[]? afterKey = null;
        StorageScanResult page;
        do
        {
            var delivered = EmptyPage;
            page = capture.View.VisitRange(prefix, maximumRecords, (key, value) =>
            {
                capture.Observe(key, value);
                afterKey = key.ToArray();
                delivered++;
                return delivered < maximumRecords;
            }, afterKey, cancellationToken: token);
        }
        while (page.StoppedByVisitor);
    }
}
