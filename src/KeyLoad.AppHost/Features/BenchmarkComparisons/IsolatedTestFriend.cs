using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo(KeyLoad.AppHost.Features.BenchmarkComparisons.IsolatedTestFriend.AssemblyName)]

namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal static class IsolatedTestFriend
{
    internal const string AssemblyName = "KeyLoad.ComparisonTests";
}
