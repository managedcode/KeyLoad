using System;
using Microsoft.CodeAnalysis;

namespace KeyLoad.Analyzers.CodeQuality;

internal static class ZoneTreeAccessPolicy
{
    internal const string DiagnosticId = "KLD0039";
    internal const string Title = "Await ZoneTree work without blocking or thread-pool dispatch";
    internal const string Message = "'{0}' must await ZoneTree work instead of blocking on it or dispatching it to the thread pool";
    internal const string Description = "ZoneTree, ZoneTree.FullTextSearch and ZoneTree.Analytics asynchronous APIs, such as durable commits and write-ahead log syncs, are awaited so they never block a grain or thread-pool thread. Blocking on their tasks or wrapping engine work in a thread-pool dispatch is rejected.";
    private const string TasksNamespace = "System.Threading.Tasks";
    private const string CollectionsNamespace = "System.Collections.Generic";

    internal static bool Execution(ISymbol? symbol) => symbol?.ContainingAssembly?.Name is
        "ZoneTree" or "ZoneTree.FullTextSearch" or "ZoneTree.Analytics";

    internal static bool Asynchronous(IMethodSymbol method) => method.IsAsync ||
        method.Name.EndsWith("Async", StringComparison.Ordinal) || Awaitable(method.ReturnType);

    internal static bool Awaitable(ITypeSymbol type) =>
        type.ContainingNamespace?.ToDisplayString() == TasksNamespace && type.Name is "Task" or "ValueTask" ||
        type.ContainingNamespace?.ToDisplayString() == CollectionsNamespace && type.Name == "IAsyncEnumerable";

    internal static bool Blocking(IMethodSymbol method) =>
        method.Name == "GetResult" && method.ContainingType.Name is "TaskAwaiter" or "ValueTaskAwaiter" or "ConfiguredTaskAwaiter"
            or "ConfiguredValueTaskAwaiter" ||
        method.Name == "Wait" && method.ContainingType.Name is "Task" && method.ContainingNamespace.ToDisplayString() == TasksNamespace;

    internal static bool Dispatch(IMethodSymbol method) =>
        method.ContainingNamespace.ToDisplayString() == TasksNamespace &&
        (method.ContainingType.Name == "Task" && method.Name == "Run" ||
         method.ContainingType.Name == "TaskFactory" && method.Name == "StartNew") ||
        method.ContainingType.ToDisplayString() == "System.Threading.ThreadPool" &&
        method.Name is "QueueUserWorkItem" or "UnsafeQueueUserWorkItem";
}
