using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo(KeyLoad.ComparisonHost.Features.BenchmarkComparisons.ComparisonHostTestFriend.AssemblyName)]

namespace KeyLoad.ComparisonHost.Features.BenchmarkComparisons;

internal static class ComparisonHostTestFriend
{
    internal const string AssemblyName = "KeyLoad.ComparisonTests";
}
