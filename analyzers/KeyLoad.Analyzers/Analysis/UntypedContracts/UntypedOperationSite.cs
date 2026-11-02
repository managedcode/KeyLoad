using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace KeyLoad.Analyzers.CodeQuality;

internal static class UntypedOperationSite
{
    internal static bool IsRuntime(IOperation operation)
    {
        for (var parent = operation.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is INameOfOperation)
            {
                return false;
            }
        }
        return true;
    }
}
