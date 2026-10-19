using ZoneTree;
using ZoneTree.FullTextSearch;

namespace KeyLoad.Server.Features.Search;

/// <summary>Borrowed read ownership; the generation slot alone disposes its native index.</summary>
internal interface INativeTextSelectedIndexLeaseOwner
{
    void Enter(KeyLoad.Core.ReadExecutionBudget budget, NativeTextSelectedProjectionLease reader);
    IZoneTreeIterator<CompositeKeyOfTokenRecordPrevious<ulong, ulong>, byte> CreateIterator();
    void ObserveOriginalPostingRead();
    void Exit(NativeTextSelectedProjectionLease reader);
}
