using System.Collections.Immutable;

namespace KeyLoad.Core.Features.Search;

internal sealed record AnnProjectionWorkPage(ImmutableArray<AnnProjectionWork> Items, byte[]? AfterKey,
    ImmutableArray<ErrorCode> Rejected);
