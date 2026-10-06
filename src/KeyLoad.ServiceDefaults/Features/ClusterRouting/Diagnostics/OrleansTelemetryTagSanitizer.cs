using System.Diagnostics;
using KeyLoad.ServiceDefaults.Features.ClusterRouting.Contracts;

namespace KeyLoad.ServiceDefaults.Features.ClusterRouting.Diagnostics;

internal static class OrleansTelemetryTagSanitizer
{
    private const int EmptyStringLength = 0;
    internal static void Scrub(Activity activity, int maximumTagValueCharacters)
    {
        foreach (var (key, value) in activity.TagObjects.ToArray())
        {
            var normalized = Normalize(key, value, maximumTagValueCharacters);
            if (normalized is null)
            {
                activity.SetTag(key, null);
            }
            else if (!Equals(normalized, value))
            {
                activity.SetTag(key, normalized);
            }
        }
    }

    private static object? Normalize(string key, object? value, int maximumTagValueCharacters)
    {
        if (key == OrleansTelemetryPolicy.ExceptionEscapedTag && value is bool escaped)
        {
            return escaped;
        }

        if (value is not string text || text.Length == EmptyStringLength || text.Length > maximumTagValueCharacters)
        {
            return null;
        }

        return key switch
        {
            OrleansTelemetryPolicy.RpcSystemTag when string.Equals(text,
                OrleansTelemetryPolicy.RpcSystemValue, StringComparison.Ordinal) => OrleansTelemetryPolicy.RpcSystemValue,
            OrleansTelemetryPolicy.RpcServiceTag => OrleansTelemetryPolicy.RpcServiceValue,
            OrleansTelemetryPolicy.RpcMethodTag => NormalizeMethod(text),
            OrleansTelemetryPolicy.GrainTypeTag => OrleansTelemetryPolicy.GrainTypeValue,
            OrleansTelemetryPolicy.ActivationCauseTag when string.Equals(text,
                OrleansTelemetryPolicy.ActivationCauseNewValue, StringComparison.Ordinal)
                => OrleansTelemetryPolicy.ActivationCauseNewValue,
            OrleansTelemetryPolicy.ActivationCauseTag when string.Equals(text,
                OrleansTelemetryPolicy.ActivationCauseRehydrateValue, StringComparison.Ordinal)
                => OrleansTelemetryPolicy.ActivationCauseRehydrateValue,
            OrleansTelemetryPolicy.ExceptionTypeTag => OrleansTelemetryPolicy.ExceptionTypeValue,
            _ => null
        };
    }

    private static string NormalizeMethod(string method)
        => method switch
        {
            OrleansTelemetryPolicy.RequestStreamMethodName => OrleansTelemetryPolicy.RequestMethodValue,
            OrleansTelemetryPolicy.CapabilityMethodName => OrleansTelemetryPolicy.CapabilityMethodValue,
            _ when method.EndsWith(OrleansTelemetryPolicy.MethodNameSeparator
                + OrleansTelemetryPolicy.RequestStreamMethodName, StringComparison.Ordinal)
                => OrleansTelemetryPolicy.RequestMethodValue,
            _ when method.EndsWith(OrleansTelemetryPolicy.MethodNameSeparator
                + OrleansTelemetryPolicy.CapabilityMethodName, StringComparison.Ordinal)
                => OrleansTelemetryPolicy.CapabilityMethodValue,
            _ => OrleansTelemetryPolicy.GenericMethodValue
        };
}
