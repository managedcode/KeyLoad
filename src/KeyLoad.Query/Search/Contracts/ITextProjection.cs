using KeyLoad.Core;

namespace KeyLoad.Query.Features.Search;

/// <summary>Supplies a physical-owner text projection inside the canonical read gate.</summary>
public interface ITextProjection : IDisposable
{
    /// <summary>Admits one bounded build or current generation lease.</summary>
    /// <param name="scope">Source scope captured after persisted authorization.</param>
    /// <param name="budget">Shared operation budget.</param>
    /// <returns>A lease that must be disposed before leaving the same canonical cut.</returns>
    ITextProjectionLease Acquire(TextProjectionScope scope, ReadExecutionBudget budget);
}
