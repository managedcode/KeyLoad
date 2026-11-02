using System.Diagnostics;
using KeyLoad.Server.OrleansHosting.Contracts;

namespace KeyLoad.Server.OrleansHosting.Diagnostics;

internal static class OrleansTelemetryTagSanitizer
{
    internal static void Scrub(Activity activity, int maximumTagValueCharacters)
    {
        foreach (var (key, value) in ((activity.TagObjects /* KLD0041: Read the native Activity object-valued tags before applying the bounded Orleans telemetry privacy allowlist. */).ToArray() /* KLD0041: Snapshot native Activity tag pairs before removing or replacing tags during this privacy pass. */))
        {
            if (key == OrleansTelemetryPolicy.ExceptionEscapedTag && value is bool)
            {
                continue;
            }
            var normalized = ((Normalize(key, value, maximumTagValueCharacters) /* KLD0041: Normalize this native Activity runtime value into a bounded allowed string; the validated original exception-escaped bool is retained separately. */)).Value;
            if (normalized is null)
            {
                activity.SetTag(key, null) /* KLD0041: Use the native Activity object-valued setter to remove this unapproved tag. */;
            }
            else if ((value is not string original /* KLD0042: Require a concrete string from the native Activity tag before comparing or publishing its bounded normalized value. */) || !string.Equals(normalized, original, StringComparison.Ordinal))
            {
                activity.SetTag(key, normalized) /* KLD0041: Use the native Activity object-valued setter to publish only the bounded normalized string without boxing another value. */;
            }
        }
    }

    private static (bool HasValue, string? Value) Normalize(string key, object? /* KLD0041: Inspect only native Activity runtime tag values to require bounded strings and publish the fixed Orleans privacy allowlist. */ value, int maximumTagValueCharacters)
    {
        if ((value is not string text /* KLD0042: Require a concrete string from the native Activity tag before comparing or publishing its bounded normalized value. */) || text.Length == 0 || text.Length > maximumTagValueCharacters)
        {
            return (false, default(string?));
        }

        return key switch
        {
            OrleansTelemetryPolicy.RpcSystemTag when string.Equals(text,
                OrleansTelemetryPolicy.RpcSystemValue, StringComparison.Ordinal) => (true, ((string?)(OrleansTelemetryPolicy.RpcSystemValue))),
            OrleansTelemetryPolicy.RpcServiceTag => (true, ((string?)(OrleansTelemetryPolicy.RpcServiceValue))),
            OrleansTelemetryPolicy.RpcMethodTag => (true, ((string?)(NormalizeMethod(text)))),
            OrleansTelemetryPolicy.GrainTypeTag => (true, ((string?)(OrleansTelemetryPolicy.GrainTypeValue))),
            OrleansTelemetryPolicy.ActivationCauseTag when string.Equals(text,
                OrleansTelemetryPolicy.ActivationCauseNewValue, StringComparison.Ordinal)
                => (true, ((string?)(OrleansTelemetryPolicy.ActivationCauseNewValue))),
            OrleansTelemetryPolicy.ActivationCauseTag when string.Equals(text,
                OrleansTelemetryPolicy.ActivationCauseRehydrateValue, StringComparison.Ordinal)
                => (true, ((string?)(OrleansTelemetryPolicy.ActivationCauseRehydrateValue))),
            OrleansTelemetryPolicy.ExceptionTypeTag => (true, ((string?)(OrleansTelemetryPolicy.ExceptionTypeValue))),
            _ => (false, default(string?))
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
