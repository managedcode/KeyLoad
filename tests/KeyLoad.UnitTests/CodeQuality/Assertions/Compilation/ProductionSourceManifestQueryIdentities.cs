using System.Text.Json;

namespace KeyLoad.UnitTests.Features.CodeQuality.Assertions;

internal static class ProductionSourceManifestQueryIdentities
{
    private const string Contributors = "contributors";
    private const string ExactCases = "exactCases";
    private const string ClassName = "className";
    private const string MethodName = "methodName";
    private const string InstanceName = "instanceName";

    internal static HashSet<string> Read(JsonElement queryContract)
        => queryContract.GetProperty(Contributors).GetProperty(ExactCases).EnumerateArray()
            .Select(Identity).ToHashSet(StringComparer.Ordinal);

    internal static string Identity(JsonElement row)
    {
        var className = row.GetProperty(ClassName).GetString()!;
        var methodName = row.GetProperty(MethodName).GetString()!;
        var instanceName = row.GetProperty(InstanceName).GetString()!;
        return string.Concat(className.Length, ":", className,
            methodName.Length, ":", methodName, instanceName.Length, ":", instanceName);
    }
}
