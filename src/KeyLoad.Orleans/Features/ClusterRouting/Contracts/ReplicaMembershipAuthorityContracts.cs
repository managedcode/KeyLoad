using System.Collections.Immutable;

namespace KeyLoad.Orleans;

internal static class ReplicaMembershipAuthorityAliases
{
    internal const string Entry = "keyload.orleans.membership.authority.entry.v1";
    internal const string Call = "keyload.orleans.membership.authority.call.v1";
    internal const string Reply = "keyload.orleans.membership.authority.reply.v1";
}

internal enum ReplicaMembershipAuthorityOperation : int
{
    ReadAll = 1,
    ReadRow = 2,
    InsertRow = 3,
    UpdateRow = 4,
    UpdateIAmAlive = 5,
    CleanupDefunct = 6
}

internal enum ReplicaMembershipAuthorityResultKind : int
{
    Completed = 1,
    Failed = 2
}

internal enum ReplicaMembershipAuthorityErrorDetailCode : int
{
    None = 0,
    MalformedRequest = 1,
    AuthenticationFailed = 2,
    UnsupportedVersion = 3,
    AuthorityMismatch = 4,
    CallerGroupMismatch = 5,
    UnsupportedOperation = 6,
    RequestLimit = 7,
    ReplyLimit = 8,
    MembershipCapacity = 9,
    AuthorityUnavailable = 10,
    PersistedTableCorrupt = 11,
    DeleteUnsupported = 12
}

[global::Orleans.GenerateSerializer, global::Orleans.Alias(ReplicaMembershipAuthorityAliases.Entry)]
internal sealed record ReplicaMembershipAuthorityEntryV1(
    [property: global::Orleans.Id(0)] string Address,
    [property: global::Orleans.Id(1)] SiloStatus Status,
    [property: global::Orleans.Id(2)] int ProxyPort,
    [property: global::Orleans.Id(3)] string Host,
    [property: global::Orleans.Id(4)] string Name,
    [property: global::Orleans.Id(5)] DateTime Started,
    [property: global::Orleans.Id(6)] DateTime Alive,
    [property: global::Orleans.Id(7)] ImmutableArray<ReplicaMembershipSuspect> Suspects,
    [property: global::Orleans.Id(8)] string RowETag);

[global::Orleans.GenerateSerializer, global::Orleans.Alias(ReplicaMembershipAuthorityAliases.Call)]
internal sealed record ReplicaMembershipAuthorityCallV1(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] string ClusterId,
    [property: global::Orleans.Id(2)] Guid AuthorityPhysicalShardId,
    [property: global::Orleans.Id(3)] Guid AuthorityIncarnation,
    [property: global::Orleans.Id(4)] Guid CallerPhysicalShardId,
    [property: global::Orleans.Id(5)] Guid CallerIncarnation,
    [property: global::Orleans.Id(6)] string CallerVoterId,
    [property: global::Orleans.Id(7)] string CallerSiloAddress,
    [property: global::Orleans.Id(8)] Guid RequestId,
    [property: global::Orleans.Id(9)] int Operation,
    [property: global::Orleans.Id(10)] string? TargetSiloAddress,
    [property: global::Orleans.Id(11)] ReplicaMembershipAuthorityEntryV1? CandidateEntry,
    [property: global::Orleans.Id(12)] int ExpectedTableVersion,
    [property: global::Orleans.Id(13)] string? ExpectedTableVersionETag,
    [property: global::Orleans.Id(14)] string? ExpectedRowETag,
    [property: global::Orleans.Id(15)] long CleanupBeforeUtcTicks);

[global::Orleans.GenerateSerializer, global::Orleans.Alias(ReplicaMembershipAuthorityAliases.Reply)]
internal sealed record ReplicaMembershipAuthorityReplyV1(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] Guid AuthorityPhysicalShardId,
    [property: global::Orleans.Id(2)] Guid AuthorityIncarnation,
    [property: global::Orleans.Id(3)] Guid RequestId,
    [property: global::Orleans.Id(4)] string RequestNonce,
    [property: global::Orleans.Id(5)] int ResultKind,
    [property: global::Orleans.Id(6)] ErrorCode? ErrorCode,
    [property: global::Orleans.Id(7)] int ErrorDetailCode,
    [property: global::Orleans.Id(8)] bool Applied,
    [property: global::Orleans.Id(9)] int TableVersion,
    [property: global::Orleans.Id(10)] string TableVersionETag,
    [property: global::Orleans.Id(11)] ImmutableArray<ReplicaMembershipAuthorityEntryV1> Rows);
