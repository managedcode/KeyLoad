namespace KeyLoad.CrashHost.Features.ClusterRouting;

internal static class C1SubmissionOutcomeInspectionValidation
{
    private const int MinimumFormatVersion = 1;
    private const int MinimumPosition = 0;
    private const int EmptyBufferBytes = 0;
    internal static C1SubmissionOutcomeInspectionRequest Request(C1SubmissionOutcomeInspectionRequest? request)
    {
        if (request is null || request.Version != C1SubmissionOutcomeInspectionProtocol.Version
            || request.PrincipalId is null
            || !request.PrincipalId.StartsWith(C1SubmissionOutcomeInspectionProtocol.PrincipalPrefix, StringComparison.Ordinal))
        { throw Invalid(); }
        _ = C1OutcomeInspectionRequestValidation.Validate(new(C1OutcomeInspectionProtocol.Version,
            request.Directory, request.ExpectedNodeId, request.Incarnation, request.PrincipalId,
            request.CommandId, request.Partition));
        return request;
    }

    internal static C1SubmissionOutcomeInspectionReceipt Receipt(C1SubmissionOutcomeInspectionReceipt? receipt)
    {
        if (receipt is null || receipt.Version != C1SubmissionOutcomeInspectionProtocol.Version
            || receipt.NodeId == Guid.Empty || receipt.Incarnation == Guid.Empty || receipt.FormatVersion < MinimumFormatVersion
            || receipt.Position < MinimumPosition || receipt.OutcomeKey is null || receipt.NativeStoredOutcome is null
            || receipt.OutcomeKey.Length is <= EmptyBufferBytes or > C1SubmissionOutcomeInspectionProtocol.MaximumNativeRecordBytes
            || receipt.NativeStoredOutcome.Length is <= EmptyBufferBytes or > C1SubmissionOutcomeInspectionProtocol.MaximumNativeRecordBytes)
        { throw Invalid(); }
        return receipt;
    }

    internal static InvalidDataException Invalid() => new(C1SubmissionOutcomeInspectionProtocol.Invalid);
}
