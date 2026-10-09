using KeyLoad.Replication;
using Microsoft.Extensions.Options;

namespace KeyLoad.Orleans;

internal sealed class ReplicaMembershipAuthorityClientTable : IMembershipTable, IAsyncDisposable
{
    private const int NewCallExpectedTableVersionEmptyCount = 0;
    private const int NewCallCleanupBeforeUtcTicksEmptyCount = 0;

    private readonly ReplicaMembershipAuthorityClientResources resources;
    private readonly ReplicaMembershipAuthorityExchangeOptions options;
    private readonly TimeProvider clock;
    private readonly OrleansMembershipOptions settings;
    private readonly IOptions<OrleansMembershipOptions> membershipOptions;

    internal ReplicaMembershipAuthorityClientTable(ReplicaMembershipAuthorityExchangeOptions options,
        IOptions<OrleansMembershipOptions> membershipOptions, IOptions<ReplicaExecutionOptions> executionOptions)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.options = options;
        clock = options.Clock;
        this.membershipOptions = membershipOptions;
        settings = membershipOptions.Value;
        resources = new(options, membershipOptions, executionOptions);
    }

    public Task InitializeMembershipTable(bool tryInitTableVersion)
        => InitializeMembershipTableAsync(tryInitTableVersion, CancellationToken.None);

    public async Task InitializeMembershipTableAsync(bool tryInitTableVersion, CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(settings.StartupTimeout, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, cancellationToken);
        while (true)
        {
            linked.Token.ThrowIfCancellationRequested();
            try
            { _ = await ReadAllAsync(linked.Token).ConfigureAwait(false); return; }
            catch (KeyLoadException error) when (error.Code is ErrorCode.OwnershipLost or ErrorCode.UnknownWriteOutcome)
            { await Task.Delay(settings.StartupRetryDelay, clock, linked.Token).ConfigureAwait(false); }
        }
    }

    public Task DeleteMembershipTableEntries(string clusterId)
        => DeleteMembershipTableEntriesAsync(clusterId, CancellationToken.None);

    public Task DeleteMembershipTableEntriesAsync(string clusterId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = clusterId;
        return Task.FromException(Errors.Fail(ErrorCode.UnsupportedCapability, ReplicaMembershipAuthorityText.DeleteUnsupported));
    }

    public Task CleanupDefunctSiloEntries(DateTimeOffset beforeDate)
        => CleanupDefunctSiloEntriesAsync(beforeDate, CancellationToken.None);

    public async Task CleanupDefunctSiloEntriesAsync(DateTimeOffset beforeDate, CancellationToken cancellationToken)
    {
        var call = NewCall(ReplicaMembershipAuthorityOperation.CleanupDefunct) with
        { CleanupBeforeUtcTicks = beforeDate.UtcDateTime.Ticks };
        _ = await SendAsync(call, cancellationToken).ConfigureAwait(false);
    }

    public Task<MembershipTableData> ReadRow(SiloAddress key) => ReadRowAsync(key, CancellationToken.None);

    public async Task<MembershipTableData> ReadRowAsync(SiloAddress key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        var call = NewCall(ReplicaMembershipAuthorityOperation.ReadRow) with { TargetSiloAddress = key.ToParsableString() };
        return Data(await SendAsync(call, cancellationToken).ConfigureAwait(false));
    }

    public Task<MembershipTableData> ReadAll() => ReadAllAsync(CancellationToken.None);

    public async Task<MembershipTableData> ReadAllAsync(CancellationToken cancellationToken)
    {
        var reply = await SendAsync(NewCall(ReplicaMembershipAuthorityOperation.ReadAll), cancellationToken).ConfigureAwait(false);
        return Data(reply);
    }

    public Task<bool> InsertRow(MembershipEntry entry, TableVersion tableVersion)
        => InsertRowAsync(entry, tableVersion, CancellationToken.None);

    public async Task<bool> InsertRowAsync(MembershipEntry entry, TableVersion tableVersion, CancellationToken cancellationToken)
    {
        ReplicaMembershipProtocol.ValidateEntry(entry);
        ArgumentNullException.ThrowIfNull(tableVersion);
        var call = NewCall(ReplicaMembershipAuthorityOperation.InsertRow) with
        {
            CandidateEntry = ReplicaMembershipAuthorityMapping.ToWire(entry, ReplicaMembershipAuthorityProtocol.InitialRowETag),
            ExpectedTableVersion = tableVersion.Version,
            ExpectedTableVersionETag = tableVersion.VersionEtag
        };
        return (await SendAsync(call, cancellationToken).ConfigureAwait(false)).Applied;
    }

    public Task<bool> UpdateRow(MembershipEntry entry, string etag, TableVersion tableVersion)
        => UpdateRowAsync(entry, etag, tableVersion, CancellationToken.None);

    public async Task<bool> UpdateRowAsync(MembershipEntry entry, string etag, TableVersion tableVersion,
        CancellationToken cancellationToken)
    {
        ReplicaMembershipProtocol.ValidateEntry(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(etag);
        ArgumentNullException.ThrowIfNull(tableVersion);
        var address = entry.SiloAddress.ToParsableString();
        var call = NewCall(ReplicaMembershipAuthorityOperation.UpdateRow) with
        {
            TargetSiloAddress = address,
            CandidateEntry = ReplicaMembershipAuthorityMapping.ToWire(entry, etag),
            ExpectedRowETag = etag,
            ExpectedTableVersion = tableVersion.Version,
            ExpectedTableVersionETag = tableVersion.VersionEtag
        };
        return (await SendAsync(call, cancellationToken).ConfigureAwait(false)).Applied;
    }

    public Task UpdateIAmAlive(MembershipEntry entry) => UpdateIAmAliveAsync(entry, CancellationToken.None);

    public async Task UpdateIAmAliveAsync(MembershipEntry entry, CancellationToken cancellationToken)
    {
        ReplicaMembershipProtocol.ValidateEntry(entry);
        var call = NewCall(ReplicaMembershipAuthorityOperation.UpdateIAmAlive) with
        { CandidateEntry = ReplicaMembershipAuthorityHeartbeat.ToWire(entry) };
        _ = await SendAsync(call, cancellationToken).ConfigureAwait(false);
    }

    private Task<ReplicaMembershipAuthorityReplyV1> SendAsync(ReplicaMembershipAuthorityCallV1 call,
        CancellationToken cancellationToken) => resources.SendAsync(call, clock, cancellationToken);

    private ReplicaMembershipAuthorityCallV1 NewCall(ReplicaMembershipAuthorityOperation operation)
        => new(ReplicaMembershipAuthorityProtocol.Version, options.ClusterId, options.AuthorityPhysicalShardId,
            options.AuthorityIncarnation, options.CallerPhysicalShardId, options.CallerIncarnation,
            options.CallerVoterId, options.CallerSiloAddress, Guid.NewGuid(), (int)operation,
            null, null, NewCallExpectedTableVersionEmptyCount, null, null, NewCallCleanupBeforeUtcTicksEmptyCount);

    private MembershipTableData Data(ReplicaMembershipAuthorityReplyV1 reply)
    {
        var rows = reply.Rows.Select(row => Tuple.Create(ReplicaMembershipAuthorityMapping.ToNative(entry: row, membershipOptions: membershipOptions), row.RowETag)).ToList();
        return new(rows, new TableVersion(reply.TableVersion, reply.TableVersionETag));
    }

    public ValueTask DisposeAsync() => resources.DisposeAsync();
}
