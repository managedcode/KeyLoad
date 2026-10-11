namespace KeyLoad.Core.Features.Authorization;

internal sealed class RowVisibilityDraft : IDisposable
{
    private readonly RowVisibilityCache owner;
    private RowVisibilityEntry? owned;

    internal RowVisibilityDraft(RowVisibilityCache owner, ICacheMemoryBudget budget,
        RowVisibilityScope scope, ICacheMemoryReservation charge)
    {
        this.owner = owner;
        owned = new(scope, budget, charge);
    }

    internal void AddVisible(string id)
    {
        if (owned is { } current && !current.Add(id))
        { Dispose(); }
    }

    internal void Complete()
    {
        if (owned is { } current && owner.Publish(current))
        { owned = null; }
    }

    public void Dispose()
    {
        owned?.Dispose();
        owned = null;
    }
}
