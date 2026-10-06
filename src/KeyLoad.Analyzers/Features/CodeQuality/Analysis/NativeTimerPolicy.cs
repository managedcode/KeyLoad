using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace KeyLoad.Analyzers.Features.CodeQuality;

internal static class NativeTimerPolicy
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
         method.Name == Change && IsNative(compilation, method.ContainingType, ThreadingTimer));

    internal static bool IsDurationArgument(Compilation compilation, IMethodSymbol method, IArgumentOperation argument) =>
        !IsTimer(compilation, method.ContainingType) ||
        argument.Parameter?.Name is DueTime or Period or Interval;

    internal static bool IsOperationalProperty(Compilation compilation, IPropertySymbol property) =>
        property.Name is nameof(System.Threading.PeriodicTimer.Period) or nameof(System.Timers.Timer.Interval) &&
        (IsNative(compilation, property.ContainingType, PeriodicTimer) ||
         IsNative(compilation, property.ContainingType, TimersTimer));

    private static bool IsTimer(Compilation compilation, ITypeSymbol type) =>
        IsNative(compilation, type, ThreadingTimer) || IsNative(compilation, type, PeriodicTimer) ||
        IsNative(compilation, type, TimersTimer);

    private static bool IsNative(Compilation compilation, ITypeSymbol type, string name) =>
        MagicRuntimeOperations.IsNativeMetadataType(compilation, type, name);
}
