namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class IsolatedPlanDirectory : IDisposable
{
    internal string Root { get; } = Path.Combine(Path.GetTempPath(), $"keyload-isolated-plan-{Guid.NewGuid():N}");

    internal IsolatedPlanDirectory() => Directory.CreateDirectory(Root);

    internal string PathFor(string name) => Path.Combine(Root, name);

    public void Dispose() => Directory.Delete(Root, recursive: true);
}
