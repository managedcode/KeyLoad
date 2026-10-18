using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo(KeyLoad.LoadTests.AppHost.Features.BenchmarkComparisons.IsolatedTestFriend.AssemblyName)]
[assembly: InternalsVisibleTo("KeyLoad.IntegrationTests")]

namespace KeyLoad.LoadTests.AppHost.Features.BenchmarkComparisons;

internal static class IsolatedTestFriend
{
    internal const string AssemblyName = "KeyLoad.ComparisonTests";
}
