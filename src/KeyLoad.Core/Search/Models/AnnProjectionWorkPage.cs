using System.Collections.Immutable;

namespace KeyLoad.Core.Search;

internal sealed record AnnProjectionWorkPage(ImmutableArray<AnnProjectionWork> Items, byte[]? AfterKey,
    ImmutableArray<ErrorCode> Rejected);
