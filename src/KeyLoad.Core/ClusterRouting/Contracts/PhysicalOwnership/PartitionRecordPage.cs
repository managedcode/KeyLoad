using System.Collections.Immutable;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

/// <summary>Owns one bounded page of exact canonical partition records.</summary>
/// <param name="Records">Independent raw key/value copies in encoded order.</param>
/// <param name="HasMore">Whether the charged native lookahead found another live record.</param>
/// <param name="RetainedBytes">Owned record key/value bytes plus the independent continuation buffer retained by this page.</param>
/// <param name="ExaminedBytes">Native examined bytes, including lookahead work.</param>
/// <param name="Continuation">An independently owned exclusive continuation key, when present.</param>
internal sealed record PartitionRecordPage(
    ImmutableArray<KeyValueRecord> Records,
    bool HasMore,
    long RetainedBytes,
    long ExaminedBytes,
    ReadOnlyMemory<byte>? Continuation);
