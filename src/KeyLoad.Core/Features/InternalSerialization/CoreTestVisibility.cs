using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo(KeyLoad.Core.Features.InternalSerialization.CoreTestVisibility.UnitTestsAssembly)]

namespace KeyLoad.Core.Features.InternalSerialization;

internal static class CoreTestVisibility
{
    internal const string UnitTestsAssembly = "KeyLoad.UnitTests";
}
