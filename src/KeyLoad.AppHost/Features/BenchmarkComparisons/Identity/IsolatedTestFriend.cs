using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo(KeyLoad.AppHost.Features.BenchmarkComparisons.IsolatedTestFriend.AssemblyName)]
[assembly: InternalsVisibleTo("KeyLoad.IntegrationTests")]

namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal static class IsolatedTestFriend
{
    internal const string AssemblyName = "KeyLoad.ComparisonTests";
}
