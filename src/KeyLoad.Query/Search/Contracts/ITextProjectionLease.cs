using KeyLoad.Core;

namespace KeyLoad.Query.Search;

/// <summary>Observes canonical tokenization and verifies complete execution candidate coverage.</summary>
public interface ITextProjectionLease : IDisposable
{
    /// <summary>Begins the next visible canonical record in storage visitation order.</summary>
    /// <param name="reference">Canonical identity.</param>
    /// <param name="revision">Canonical current revision.</param>
    void BeginRecord(EntityRef reference, long revision);
    /// <summary>Observes one already charged and normalized canonical token.</summary>
    /// <param name="token">The very token supplied to exact ranking.</param>
    void ObserveToken(string token);
    /// <summary>Checks the complete bounded execution token union before accepting exact ranks.</summary>
    /// <param name="terms">All distinct exact query terms.</param>
    /// <param name="references">All exact positive identities before Limit or fusion.</param>
    /// <param name="budget">The same shared operation budget.</param>
    void VerifyCandidates(IReadOnlyList<string> terms, IReadOnlyList<EntityRef> references,
        ReadExecutionBudget budget);
}
