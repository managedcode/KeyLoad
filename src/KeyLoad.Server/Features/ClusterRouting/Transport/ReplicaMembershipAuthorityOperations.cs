using System.Collections.Immutable;
using KeyLoad.Orleans;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class ReplicaMembershipAuthorityOperations(IOptions<NodeOptions> nodeOptions, ReplicaMembershipAuthorityMac mac, IOptions<OrleansMembershipOptions> membershipOptions)
{
    private const int DataReplyErrorDetailCodeEmptyCount = 0;
    private const int AppliedReplyErrorDetailCodeEmptyCount = 0;
    private const int AppliedReplyTableVersionEmptyCount = 0;
    private const int FailedTableVersionEmptyCount = 0;

    private readonly NodeOptions options = nodeOptions.Value;
    internal async Task<ReplicaMembershipAuthorityReplyV1> ExecuteAsync(IMembershipTable provider,
        ReplicaMembershipAuthorityCallV1 call, CancellationToken cancellationToken)
    {
        if (provider is not ReplicaMembershipTable table)
        { throw Errors.Fail(ErrorCode.OwnershipLost, ReplicaMembershipAuthorityText.Unavailable); }
        return (ReplicaMembershipAuthorityOperation)call.Operation switch
        {
            ReplicaMembershipAuthorityOperation.ReadAll => await ReadAllAsync(table, call, cancellationToken).ConfigureAwait(false),
            ReplicaMembershipAuthorityOperation.ReadRow => await ReadRowAsync(table, call, cancellationToken).ConfigureAwait(false),
            ReplicaMembershipAuthorityOperation.InsertRow => await InsertAsync(table, call, cancellationToken).ConfigureAwait(false),
            ReplicaMembershipAuthorityOperation.UpdateRow => await UpdateAsync(table, call, cancellationToken).ConfigureAwait(false),
            ReplicaMembershipAuthorityOperation.UpdateIAmAlive => await AliveAsync(table, call, cancellationToken).ConfigureAwait(false),
            ReplicaMembershipAuthorityOperation.CleanupDefunct => await CleanupAsync(table, call, cancellationToken).ConfigureAwait(false),
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, ReplicaMembershipAuthorityText.For(ErrorCode.UnsupportedCapability))
        };
    }

    private async Task<ReplicaMembershipAuthorityReplyV1> ReadAllAsync(ReplicaMembershipTable table,
        ReplicaMembershipAuthorityCallV1 call, CancellationToken token)
        => DataReply(call, await table.ReadAllAsync(token).ConfigureAwait(false));

    private async Task<ReplicaMembershipAuthorityReplyV1> ReadRowAsync(ReplicaMembershipTable table,
        ReplicaMembershipAuthorityCallV1 call, CancellationToken token)
    {
        var address = SiloAddress.FromParsableString(call.TargetSiloAddress!);
        return DataReply(call, await table.ReadRowAsync(address, token).ConfigureAwait(false));
    }

    private async Task<ReplicaMembershipAuthorityReplyV1> InsertAsync(ReplicaMembershipTable table,
        ReplicaMembershipAuthorityCallV1 call, CancellationToken token)
    {
        var entry = ReplicaMembershipAuthorityMapping.ToNative(entry: call.CandidateEntry!, membershipOptions: membershipOptions);
        var version = Version(call);
        var applied = await table.InsertRowAsync(entry, version, token).ConfigureAwait(false);
        return AppliedReply(call, applied);
    }

    private async Task<ReplicaMembershipAuthorityReplyV1> UpdateAsync(ReplicaMembershipTable table,
        ReplicaMembershipAuthorityCallV1 call, CancellationToken token)
    {
        var entry = ReplicaMembershipAuthorityMapping.ToNative(entry: call.CandidateEntry!, membershipOptions: membershipOptions);
        var version = Version(call);
        var applied = await table.UpdateRowAsync(entry, call.ExpectedRowETag!, version, token).ConfigureAwait(false);
        return AppliedReply(call, applied);
    }

    private async Task<ReplicaMembershipAuthorityReplyV1> AliveAsync(ReplicaMembershipTable table,
        ReplicaMembershipAuthorityCallV1 call, CancellationToken token)
    {
        var entry = ReplicaMembershipAuthorityHeartbeat.ToNative(entry: call.CandidateEntry!, membershipOptions: membershipOptions);
        await table.UpdateIAmAliveAsync(entry, token).ConfigureAwait(false);
        return AppliedReply(call, true);
    }

    private async Task<ReplicaMembershipAuthorityReplyV1> CleanupAsync(ReplicaMembershipTable table,
        ReplicaMembershipAuthorityCallV1 call, CancellationToken token)
    {
        var cutoff = new DateTimeOffset(call.CleanupBeforeUtcTicks, TimeSpan.Zero);
        await table.CleanupDefunctSiloEntriesAsync(cutoff, token).ConfigureAwait(false);
        return AppliedReply(call, true);
    }

    private ReplicaMembershipAuthorityReplyV1 DataReply(ReplicaMembershipAuthorityCallV1 call, MembershipTableData data)
        => new(ReplicaMembershipAuthorityProtocol.Version, options.PhysicalShardId, options.Incarnation,
            call.RequestId, string.Empty, (int)ReplicaMembershipAuthorityResultKind.Completed, null, DataReplyErrorDetailCodeEmptyCount, false,
            data.Version.Version, data.Version.VersionEtag, ReplicaMembershipAuthorityMapping.ToWireRows(data));

    private ReplicaMembershipAuthorityReplyV1 AppliedReply(ReplicaMembershipAuthorityCallV1 call, bool applied)
        => new(ReplicaMembershipAuthorityProtocol.Version, options.PhysicalShardId, options.Incarnation,
            call.RequestId, string.Empty, (int)ReplicaMembershipAuthorityResultKind.Completed, null, AppliedReplyErrorDetailCodeEmptyCount, applied,
            AppliedReplyTableVersionEmptyCount, string.Empty, ImmutableArray<ReplicaMembershipAuthorityEntryV1>.Empty);

    private static TableVersion Version(ReplicaMembershipAuthorityCallV1 call)
        => new(call.ExpectedTableVersion, call.ExpectedTableVersionETag!);

    internal ReplicaMembershipAuthorityReplyV1 Failed(ReplicaMembershipAuthorityCallV1 call, string nonce, KeyLoadException error)
        => new(ReplicaMembershipAuthorityProtocol.Version, options.PhysicalShardId, options.Incarnation,
            call.RequestId, nonce, (int)ReplicaMembershipAuthorityResultKind.Failed, error.Code,
            (int)ReplicaMembershipAuthorityMapping.Detail(error.Code), false, FailedTableVersionEmptyCount, string.Empty,
            ImmutableArray<ReplicaMembershipAuthorityEntryV1>.Empty);

    internal void VerifyCallHeaderBinding(ReplicaMembershipAuthorityCallV1 call,
        ReplicaMembershipAuthorityRequestHeaders headers)
    {
        if (call.ClusterId != headers.Cluster || call.AuthorityPhysicalShardId.ToString(ReplicaMembershipAuthorityProtocol.IdentityFormat) != headers.AuthorityPhysical
            || call.AuthorityIncarnation.ToString(ReplicaMembershipAuthorityProtocol.IdentityFormat) != headers.AuthorityIncarnation
            || call.AuthorityPhysicalShardId != options.PhysicalShardId || call.AuthorityIncarnation != options.Incarnation
            || call.CallerPhysicalShardId.ToString(ReplicaMembershipAuthorityProtocol.IdentityFormat) != headers.CallerPhysical
            || call.CallerIncarnation.ToString(ReplicaMembershipAuthorityProtocol.IdentityFormat) != headers.CallerIncarnation || call.CallerVoterId != headers.CallerVoter
        || call.CallerSiloAddress != headers.CallerSilo)
        { throw Errors.Fail(ErrorCode.Unauthenticated, ReplicaMembershipAuthorityText.InvalidIdentity); }
        VerifyOperationCaller(call);
    }

    private static void VerifyOperationCaller(ReplicaMembershipAuthorityCallV1 call)
    {
        var operation = (ReplicaMembershipAuthorityOperation)call.Operation;
        var candidate = call.CandidateEntry?.Address;
        if (operation == ReplicaMembershipAuthorityOperation.UpdateRow
            && !string.Equals(call.TargetSiloAddress, candidate, StringComparison.Ordinal))
        { throw Errors.Fail(ErrorCode.Validation, ReplicaMembershipAuthorityText.For(ErrorCode.Validation)); }
        if (operation is ReplicaMembershipAuthorityOperation.InsertRow or ReplicaMembershipAuthorityOperation.UpdateIAmAlive
            && !string.Equals(candidate, call.CallerSiloAddress, StringComparison.Ordinal))
        { throw Errors.Fail(ErrorCode.Unauthenticated, ReplicaMembershipAuthorityText.InvalidIdentity); }
    }

    internal async Task WriteReplyAsync(HttpContext context, ReplicaMembershipAuthorityCallV1 call,
        string nonce, ReplicaMembershipAuthorityReplyV1 reply)
    {
        var bound = reply with { RequestNonce = nonce };
        var body = ReplicaMembershipAuthorityCodec.SerializeReply(reply: bound, membershipOptions: membershipOptions);
        var signature = mac.SignReply(options.PhysicalShardId.ToString(ReplicaMembershipAuthorityProtocol.IdentityFormat), options.Incarnation.ToString(ReplicaMembershipAuthorityProtocol.IdentityFormat),
            call.RequestId.ToString(ReplicaMembershipAuthorityProtocol.IdentityFormat), nonce, StatusCodes.Status200OK, body);
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = ReplicaMembershipAuthorityProtocol.ContentType;
        context.Response.Headers[ReplicaMembershipAuthorityProtocol.SignatureHeader] = signature;
        context.Response.ContentLength = body.Length;
        await context.Response.Body.WriteAsync(body, context.RequestAborted).ConfigureAwait(false);
    }
}
