using System.Runtime.CompilerServices;

namespace KeyLoad.Core;

/// <summary>Stops recursive request walkers before nesting exhausts the thread's execution stack.</summary>
internal static class ExecutionStackGuard
{
    internal const string ExhaustedDetail = "The request nesting exceeds the available execution stack.";

    /// <summary>Fails the request with a validation error when too little execution stack remains for another level.</summary>
    internal static void Ensure()
    {
        if (!RuntimeHelpers.TryEnsureSufficientExecutionStack())
        {
            throw Errors.Fail(ErrorCode.Validation, ExhaustedDetail);
        }
    }
}
