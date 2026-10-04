using System.Text.Json;

namespace KeyLoad.Orleans;

internal static class GrainBoundaryErrors
{
    internal static bool Handles(Exception error) => error is KeyLoadException or OperationCanceledException
        or JsonException or ArgumentException or IOException or UnauthorizedAccessException or TimeoutException
        or InvalidOperationException or NotSupportedException or OverflowException or global::Orleans.Runtime.OrleansException;
}
