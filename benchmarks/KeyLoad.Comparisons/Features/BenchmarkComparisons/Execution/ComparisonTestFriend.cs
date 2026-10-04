using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo(KeyLoad.Comparisons.ComparisonTestFriend.AssemblyName)]
[assembly: InternalsVisibleTo(KeyLoad.Comparisons.ComparisonTestFriend.ComparisonHostAssemblyName)]
[assembly: InternalsVisibleTo(KeyLoad.Comparisons.ComparisonTestFriend.UnitTestsAssemblyName)]
[assembly: InternalsVisibleTo(KeyLoad.Comparisons.ComparisonTestFriend.AppHostAssemblyName)]

namespace KeyLoad.Comparisons;

internal static class ComparisonTestFriend
{
    internal const string AssemblyName = "KeyLoad.ComparisonTests";
    internal const string ComparisonHostAssemblyName = "KeyLoad.ComparisonHost";
    internal const string UnitTestsAssemblyName = "KeyLoad.UnitTests";
    internal const string AppHostAssemblyName = "KeyLoad.AppHost";
}
