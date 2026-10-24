namespace KeyLoad.UnitTests.Search;

/// <summary>Owns actual canonical ZoneTree state for index read-cut and publication checks.</summary>
internal sealed class CanonicalReadCutFixture : IDisposable
{
    internal CanonicalReadCutFixture(DatabaseLimits? limits = null, TimeProvider? timeProvider = null)
        => Canonical = new(limits, timeProvider: timeProvider);

    internal TestDatabase Canonical { get; }

    public void Dispose() => Canonical.Dispose();
}
