using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace KeyLoad.Analyzers.Features.CodeQuality;

internal static class TimerPolicy
{
    private const string ThreadingTimer = "System.Threading.Timer";
    private const string PeriodicTimer = "System.Threading.PeriodicTimer";
    private const string TimersTimer = "System.Timers.Timer";
    private const string Change = "Change";
    private const string DueTime = "dueTime";
    private const string Period = "period";
    private const string Interval = "interval";

    internal static bool IsDurationMethod(Compilation compilation, IMethodSymbol method) =>
        IsTimer(compilation, method.ContainingType) &&
        (method.MethodKind == MethodKind.Constructor ||
         method.Name == Change && RuntimeIs(compilation, method.ContainingType, ThreadingTimer));

    internal static bool IsDurationArgument(Compilation compilation, IMethodSymbol method, IArgumentOperation argument) =>
        !IsTimer(compilation, method.ContainingType) ||
        argument.Parameter?.Name is DueTime or Period or Interval;

    internal static bool IsOperationalProperty(Compilation compilation, IPropertySymbol property) =>
        property.Name is nameof(System.Threading.PeriodicTimer.Period) or nameof(System.Timers.Timer.Interval) &&
        (RuntimeIs(compilation, property.ContainingType, PeriodicTimer) ||
         RuntimeIs(compilation, property.ContainingType, TimersTimer));

    private static bool IsTimer(Compilation compilation, ITypeSymbol type) =>
        RuntimeIs(compilation, type, ThreadingTimer) || RuntimeIs(compilation, type, PeriodicTimer) ||
        RuntimeIs(compilation, type, TimersTimer);

    private static bool RuntimeIs(Compilation compilation, ITypeSymbol type, string name) =>
        MagicRuntimeOperations.IsMetadataType(compilation, type, name);
}
