using System;
using Microsoft.CodeAnalysis;

namespace KeyLoad.Analyzers.Features.CodeQuality;

internal static class ZoneTreeAccessPolicy
{
    internal const string DiagnosticId = "KLD0039";
    internal const string Title = "Use synchronous execution ZoneTree APIs";
    internal const string Message = "'{0}' must use the synchronous execution ZoneTree API without thread-pool dispatch";
    internal const string Description = "ZoneTree, ZoneTree.FullTextSearch and ZoneTree.Analytics access uses their public synchronous APIs. Orleans and transport calls remain asynchronous; wrapping execution engine work in a thread-pool dispatch does not satisfy this rule.";
    private const string TasksNamespace = "System.Threading.Tasks";
    private const string CollectionsNamespace = "System.Collections.Generic";

    internal static bool Execution(ISymbol? symbol) => symbol?.ContainingAssembly?.Name is
        "ZoneTree" or "ZoneTree.FullTextSearch" or "ZoneTree.Analytics";

    internal static bool Asynchronous(IMethodSymbol method) => method.IsAsync ||
        method.Name.EndsWith("Async", StringComparison.Ordinal) || Awaitable(method.ReturnType);

    internal static bool Awaitable(ITypeSymbol type) =>
        type.ContainingNamespace?.ToDisplayString() == TasksNamespace && type.Name is "Task" or "ValueTask" ||
        type.ContainingNamespace?.ToDisplayString() == CollectionsNamespace && type.Name == "IAsyncEnumerable";

    internal static bool Dispatch(IMethodSymbol method) =>
        method.ContainingNamespace.ToDisplayString() == TasksNamespace &&
        (method.ContainingType.Name == "Task" && method.Name == "Run" ||
         method.ContainingType.Name == "TaskFactory" && method.Name == "StartNew") ||
        method.ContainingType.ToDisplayString() == "System.Threading.ThreadPool" &&
        method.Name is "QueueUserWorkItem" or "UnsafeQueueUserWorkItem";
}
