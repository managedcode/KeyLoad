using ZoneTree;
using ZoneTree.FullTextSearch;

namespace KeyLoad.Server.Features.Search;

/// <summary>Borrowed read ownership; the generation slot alone disposes its execution index.</summary>
internal interface ITextSelectedIndexLeaseOwner
{
    void Enter(KeyLoad.Core.ReadExecutionBudget budget, TextSelectedProjectionLease reader);
    IZoneTreeIterator<CompositeKeyOfTokenRecordPrevious<ulong, ulong>, byte> CreateIterator();
    void ObserveOriginalPostingRead();
    void Exit(TextSelectedProjectionLease reader);
}
