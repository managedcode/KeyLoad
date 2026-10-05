using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo(KeyLoad.Core.Features.InternalSerialization.CoreTestVisibility.UnitTestsAssembly)]
[assembly: InternalsVisibleTo(KeyLoad.Core.Features.InternalSerialization.CoreTestVisibility.ReplicationAssembly)]
[assembly: InternalsVisibleTo(KeyLoad.Core.Features.InternalSerialization.CoreTestVisibility.BenchmarkScenariosAssembly)]
[assembly: InternalsVisibleTo(KeyLoad.Core.Features.InternalSerialization.CoreTestVisibility.QueryAssembly)]
[assembly: InternalsVisibleTo(KeyLoad.Core.Features.InternalSerialization.CoreTestVisibility.RecoveryTestsAssembly)]

namespace KeyLoad.Core.Features.InternalSerialization;

internal static class CoreTestVisibility
{
    internal const string UnitTestsAssembly = "KeyLoad.UnitTests";
    internal const string ReplicationAssembly = "KeyLoad.Replication";
    internal const string BenchmarkScenariosAssembly = "KeyLoad.BenchmarkScenarios";
    internal const string QueryAssembly = "KeyLoad.Query";
    internal const string RecoveryTestsAssembly = "KeyLoad.RecoveryTests";
}
