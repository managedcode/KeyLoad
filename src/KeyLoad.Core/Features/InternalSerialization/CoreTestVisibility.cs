using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo(KeyLoad.Core.Features.InternalSerialization.CoreTestVisibility.UnitTestsAssembly)]
[assembly: InternalsVisibleTo(KeyLoad.Core.Features.InternalSerialization.CoreTestVisibility.ReplicationAssembly)]

namespace KeyLoad.Core.Features.InternalSerialization;

internal static class CoreTestVisibility
{
    internal const string UnitTestsAssembly = "KeyLoad.UnitTests";
    internal const string ReplicationAssembly = "KeyLoad.Replication";
}
