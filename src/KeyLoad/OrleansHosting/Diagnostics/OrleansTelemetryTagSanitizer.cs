using System.Diagnostics;
using KeyLoad.Server.OrleansHosting.Contracts;

namespace KeyLoad.Server.OrleansHosting.Diagnostics;

internal static class OrleansTelemetryTagSanitizer
{
    internal static void Scrub(Activity activity, int maximumTagValueCharacters)
    {
        foreach (var (key, value) in activity.TagObjects.ToArray())
        {
            var normalized = (Normalize(key, value, maximumTagValueCharacters)).Value;
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

    private static (bool HasValue, object? Value) Normalize(string key, object? value, int maximumTagValueCharacters)
    {
        if (key == OrleansTelemetryPolicy.ExceptionEscapedTag && value is bool escaped)
        {
            return (true, ((object?)(escaped)));
        }

        if (value is not string text || text.Length == 0 || text.Length > maximumTagValueCharacters)
        {
            return (false, default(object?));
        }

        return key switch
        {
            OrleansTelemetryPolicy.RpcSystemTag when string.Equals(text,
                OrleansTelemetryPolicy.RpcSystemValue, StringComparison.Ordinal) => (true, ((object?)(OrleansTelemetryPolicy.RpcSystemValue))),
            OrleansTelemetryPolicy.RpcServiceTag => (true, ((object?)(OrleansTelemetryPolicy.RpcServiceValue))),
            OrleansTelemetryPolicy.RpcMethodTag => (true, ((object?)(NormalizeMethod(text)))),
            OrleansTelemetryPolicy.GrainTypeTag => (true, ((object?)(OrleansTelemetryPolicy.GrainTypeValue))),
            OrleansTelemetryPolicy.ActivationCauseTag when string.Equals(text,
                OrleansTelemetryPolicy.ActivationCauseNewValue, StringComparison.Ordinal)
                => (true, ((object?)(OrleansTelemetryPolicy.ActivationCauseNewValue))),
            OrleansTelemetryPolicy.ActivationCauseTag when string.Equals(text,
                OrleansTelemetryPolicy.ActivationCauseRehydrateValue, StringComparison.Ordinal)
                => (true, ((object?)(OrleansTelemetryPolicy.ActivationCauseRehydrateValue))),
            OrleansTelemetryPolicy.ExceptionTypeTag => (true, ((object?)(OrleansTelemetryPolicy.ExceptionTypeValue))),
            _ => (false, default(object?))
        };
    }

    private static string NormalizeMethod(string method)
        => method switch
        {
            OrleansTelemetryPolicy.RequestMethodValue => OrleansTelemetryPolicy.RequestMethodValue,
            OrleansTelemetryPolicy.CapabilityMethodValue => OrleansTelemetryPolicy.CapabilityMethodValue,
            OrleansTelemetryPolicy.RequestStreamStartMethod => OrleansTelemetryPolicy.RequestMethodValue,
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
