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

    private static global::ManagedCode.Communication.Result<object?> Normalize(string key, object? value, int maximumTagValueCharacters)
    {
        if (key == OrleansTelemetryPolicy.ExceptionEscapedTag && value is bool escaped)
        {
            return global::ManagedCode.Communication.Result<object?>.Succeed(((object?)(escaped)));
        }

        if (value is not string text || text.Length == 0 || text.Length > maximumTagValueCharacters)
        {
            return global::ManagedCode.Communication.Result<object?>.FailNotFound(nameof(Normalize));
        }

        return key switch
        {
            OrleansTelemetryPolicy.RpcSystemTag when string.Equals(text,
                OrleansTelemetryPolicy.RpcSystemValue, StringComparison.Ordinal) => global::ManagedCode.Communication.Result<object?>.Succeed(((object?)(OrleansTelemetryPolicy.RpcSystemValue))),
            OrleansTelemetryPolicy.RpcServiceTag => global::ManagedCode.Communication.Result<object?>.Succeed(((object?)(OrleansTelemetryPolicy.RpcServiceValue))),
            OrleansTelemetryPolicy.RpcMethodTag => global::ManagedCode.Communication.Result<object?>.Succeed(((object?)(NormalizeMethod(text)))),
            OrleansTelemetryPolicy.GrainTypeTag => global::ManagedCode.Communication.Result<object?>.Succeed(((object?)(OrleansTelemetryPolicy.GrainTypeValue))),
            OrleansTelemetryPolicy.ActivationCauseTag when string.Equals(text,
                OrleansTelemetryPolicy.ActivationCauseNewValue, StringComparison.Ordinal)
                => global::ManagedCode.Communication.Result<object?>.Succeed(((object?)(OrleansTelemetryPolicy.ActivationCauseNewValue))),
            OrleansTelemetryPolicy.ActivationCauseTag when string.Equals(text,
                OrleansTelemetryPolicy.ActivationCauseRehydrateValue, StringComparison.Ordinal)
                => global::ManagedCode.Communication.Result<object?>.Succeed(((object?)(OrleansTelemetryPolicy.ActivationCauseRehydrateValue))),
            OrleansTelemetryPolicy.ExceptionTypeTag => global::ManagedCode.Communication.Result<object?>.Succeed(((object?)(OrleansTelemetryPolicy.ExceptionTypeValue))),
            _ => global::ManagedCode.Communication.Result<object?>.FailNotFound(nameof(Normalize))
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
