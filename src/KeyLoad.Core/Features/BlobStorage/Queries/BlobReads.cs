using KeyLoad.Storage;

namespace KeyLoad.Core.Features.BlobStorage;

internal sealed class BlobReads(DatabaseEngine database, TimeProvider clock)
{
    private BlobRecordReader Reader => new(database.Store.Identity.Incarnation);
    private BlobAuthority Authority => new(database);

    internal T Cut<T>(string principalId, Func<IKeyValueView, PrincipalRecord, T> read, CancellationToken cancellationToken)
    {
        var budget = new ReadExecutionBudget(database.OperationLimitsOptions, clock, cancellationToken);
        return database.Store.Read(view =>
        {
            var bounded = budget.CreateView(view);
            var principal = database.Principal(bounded, principalId, clock.GetUtcNow());
            var result = read(bounded, principal);
            budget.CheckResult(result);
            return result;
        });
    }

    internal BlobMetadata? Metadata(IKeyValueView view, PrincipalRecord principal, BlobMetadataRequest request)
    {
        const int EmptyRevision = 0;

        _ = Authority.Scope(view, principal, request.Blob, Capability.BlobRead);
        var head = Reader.Head(view, request.Blob);
        if (head is null)
        { return null; }
        Authority.ReadRow(principal, head.Metadata.Access);
        if (head.Metadata.Revision == EmptyRevision)
        { return null; }
        return head.Metadata;
    }

    internal BlobUploadInfo? UploadInfo(IKeyValueView view, PrincipalRecord principal, BlobUploadInfoRequest request)
    {
        if (request.UploadId == Guid.Empty)
        { throw BlobErrors.Validation(); }
        _ = Authority.Scope(view, principal, request.Blob, Capability.BlobWrite);
        var head = Reader.Head(view, request.Blob);
        var state = Reader.State(view, request.Blob, request.UploadId);
        if (head is null && state is not null)
        { throw BlobErrors.Corruption(); }
        Authority.WriteState(principal, state);
        return state?.Info;
    }

    internal BlobReadResult Range(IKeyValueView view, PrincipalRecord principal, BlobReadRequest request)
    {
        const int ExpectedRevisionValidationBoundary = 0;
        const int OffsetValidationBoundary = 0;
        const int RequestCountEmptyCount = 0;

        if (request.ExpectedRevision <= ExpectedRevisionValidationBoundary || request.Offset < OffsetValidationBoundary || request.Count is < RequestCountEmptyCount or > BlobLimits.MaxRangeBytes)
        { throw BlobErrors.Validation(); }
        _ = Authority.Scope(view, principal, request.Blob, Capability.BlobRead);
        var head = Reader.Head(view, request.Blob);
        if (head is null || head.Metadata.VersionId is null)
        { throw Errors.Fail(ErrorCode.NotFound, BlobErrors.Missing); }
        Authority.ReadRow(principal, head.Metadata.Access);
        if (request.ExpectedRevision != head.Metadata.Revision)
        { throw Errors.Fail(ErrorCode.RevisionConflict, BlobErrors.Revision); }
        var state = Reader.Current(view, head);
        if (request.Offset > state.DeclaredLength || request.Count > state.DeclaredLength - request.Offset)
        { throw BlobErrors.Validation(); }
        var result = new byte[request.Count];
        CopyParts(view, state, request.Offset, result);
        return new(head.Metadata, request.Offset, result);
    }

    private static void CopyParts(IKeyValueView view, BlobState state, long offset, byte[] result)
    {
        const int CopiedInitialValue = 0;

        var copied = CopiedInitialValue;
        while (copied < result.Length)
        {
            var position = offset + copied;
            var ordinal = checked((int)(position / BlobLimits.RawPartBytes));
            var inside = checked((int)(position % BlobLimits.RawPartBytes));
            BlobRecordReader.Part(view, state, ordinal, bytes =>
            {
                var count = Math.Min(bytes.Length - inside, result.Length - copied);
                bytes.Slice(inside, count).CopyTo(result.AsSpan(copied));
                copied += count;
            });
        }
    }

    internal BlobListPage List(IKeyValueView view, PrincipalRecord principal, BlobListRequest request)
    {
        const int LimitFirstCount = 1;

        if (request.Limit is < LimitFirstCount or > BlobLimits.MaxListItems)
        { throw BlobErrors.Validation(); }
        if (request.AfterId is not null)
        { JsonData.Identifier(request.AfterId); }
        var scope = new BlobRef(request.Partition, request.Resource, request.AfterId ?? BlobListAccumulator.ScopeId);
        _ = Authority.Scope(view, principal, scope, Capability.BlobRead);
        var accumulator = new BlobListAccumulator(database, principal, request.Limit);
        var after = request.AfterId is null ? null : BlobKeys.Head(scope);
        var scan = view.VisitRange(BlobKeys.Heads(request.Partition, request.Resource), request.Limit * BlobListAccumulator.CandidateMultiplier,
            (key, value) => accumulator.Visit(view, key, value), after);
        return new([.. accumulator.Items], scan.StoppedByVisitor ? accumulator.LastId : null);
    }
}

internal sealed class BlobListAccumulator(DatabaseEngine database, PrincipalRecord principal, int limit)
{
    internal const string ScopeId = "blob-list-scope";
    internal const int CandidateMultiplier = 2;
    private int visited;
    internal List<BlobMetadata> Items { get; } = [];
    internal string? LastId { get; private set; }

    internal bool Visit(IKeyValueView view, ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        const int RevisionValidationBoundary = 0;

        var blob = BlobKeys.DecodeScope(key, BlobKeys.HeadSpace, BlobKeys.ScopeComponents);
        var head = BlobRecordReader.Decode<BlobHead>(value);
        BlobRecordReader.ValidateHead(head, blob, database.Store.Identity.Incarnation);
        visited++;
        LastId = blob.Id;
        if (head.Metadata.Revision > RevisionValidationBoundary && !head.Metadata.Deleted
            && database.Authorization.CanReadRow(principal, head.Metadata.Access))
        {
            _ = new BlobRecordReader(database.Store.Identity.Incarnation).Current(view, head);
            Items.Add(head.Metadata);
        }
        return visited < limit * CandidateMultiplier && Items.Count < limit;
    }
}
