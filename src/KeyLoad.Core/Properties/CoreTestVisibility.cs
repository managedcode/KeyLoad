using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo(KeyLoad.Core.InternalSerialization.CoreTestVisibility.UnitTestsAssembly)]
[assembly: InternalsVisibleTo(KeyLoad.Core.InternalSerialization.CoreTestVisibility.IntegrationTestsAssembly)]
[assembly: InternalsVisibleTo(KeyLoad.Core.InternalSerialization.CoreTestVisibility.BenchmarkScenariosAssembly)]
[assembly: InternalsVisibleTo(KeyLoad.Core.InternalSerialization.CoreTestVisibility.QueryAssembly)]
[assembly: InternalsVisibleTo(KeyLoad.Core.InternalSerialization.CoreTestVisibility.RecoveryTestsAssembly)]

namespace KeyLoad.Core.InternalSerialization;

internal static class CoreTestVisibility
{
    internal const string IntegrationTestsAssembly = "KeyLoad.ClusterTests";
    internal const string UnitTestsAssembly = "KeyLoad.FunctionalTests";
    internal const string BenchmarkScenariosAssembly = "KeyLoad.BenchmarkScenarios";
    internal const string QueryAssembly = "KeyLoad.Query";
    internal const string RecoveryTestsAssembly = "KeyLoad.ProcessRecoveryTests";
}
