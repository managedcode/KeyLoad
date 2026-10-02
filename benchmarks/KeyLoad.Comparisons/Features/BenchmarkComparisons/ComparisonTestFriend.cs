using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo(KeyLoad.Comparisons.ComparisonTestFriend.AssemblyName)]
[assembly: InternalsVisibleTo(KeyLoad.Comparisons.ComparisonTestFriend.ComparisonHostAssemblyName)]

namespace KeyLoad.Comparisons;

internal static class ComparisonTestFriend
{
    internal const string AssemblyName = "KeyLoad.ComparisonTests";
    internal const string ComparisonHostAssemblyName = "KeyLoad.ComparisonHost";
}
