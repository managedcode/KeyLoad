using System.Collections.Immutable;
using KeyLoad.Core;

namespace KeyLoad.Query.Search;

/// <summary>Reads certified posting candidates from a sealed canonical corpus.</summary>
internal interface ITextCertifiedProjectionLease
{
    /// <summary>Returns the complete verified posting union and exact corpus totals for a sealed reader.</summary>
    TextPostingCandidates? ReadCertifiedCandidates(IReadOnlyList<string> terms, ReadExecutionBudget budget);

    /// <summary>Settles canonical verification and retires an inconsistent generation.</summary>
    void SettleCertifiedCandidates(bool valid);
}

/// <summary>Exact corpus totals and verified native candidates at the admitted cut.</summary>
internal sealed record TextPostingCandidates(int DocumentCount, long TotalLength,
    ImmutableArray<TextPostingCandidate> Candidates);

/// <summary>Canonical document identity and revision bound to one native posting ordinal.</summary>
internal readonly record struct TextPostingCandidate(EntityRef Reference, long Revision);
