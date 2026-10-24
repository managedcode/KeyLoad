using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo(KeyLoad.Core.InternalSerialization.CoreTestVisibility.UnitTestsAssembly)]
[assembly: InternalsVisibleTo(KeyLoad.Core.InternalSerialization.CoreTestVisibility.IntegrationTestsAssembly)]
[assembly: InternalsVisibleTo(KeyLoad.Core.InternalSerialization.CoreTestVisibility.BenchmarkScenariosAssembly)]
[assembly: InternalsVisibleTo(KeyLoad.Core.InternalSerialization.CoreTestVisibility.QueryAssembly)]
[assembly: InternalsVisibleTo(KeyLoad.Core.InternalSerialization.CoreTestVisibility.RecoveryTestsAssembly)]

namespace KeyLoad.Core.InternalSerialization;

internal static class CoreTestVisibility
{
    internal const string IntegrationTestsAssembly = "KeyLoad.IntegrationTests";
    internal const string UnitTestsAssembly = "KeyLoad.UnitTests";
    internal const string BenchmarkScenariosAssembly = "KeyLoad.BenchmarkScenarios";
    internal const string QueryAssembly = "KeyLoad.Query";
    internal const string RecoveryTestsAssembly = "KeyLoad.RecoveryTests";
}
