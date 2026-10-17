using System.Collections.Immutable;
using System.Globalization;
using System.Net;
using KeyLoad.Server;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

[ConfigurationBinding]
internal static class ControlledPartitionMovementNativeHostInputValidation
{
    private const int Version = 1;
    private const int FirstNode = 0;
    private const int SingleArgumentCount = 1;
    private const int EmptyUriComponentLength = 0;
    private const string OriginRootPath = "/";
    private const string NodePrefix = "--KeyLoad:";
    private const string UrlPrefix = "--urls=";
    private const string Assignment = "=";
    private const string RootPrefix = "keyload-movement-native-hosts-";
    private const string NodeDirectoryPrefix = "node";

    internal static (ImmutableArray<string> Origins, ImmutableArray<IPEndPoint> Silos) Read(
        ControlledPartitionMovementNativeHostInput input, string originalRoot)
    {
        if (input is null || string.IsNullOrWhiteSpace(input.Root)
            || string.IsNullOrWhiteSpace(originalRoot) || input.Version != Version || input.Arguments.IsDefault
            || input.Arguments.Length != ControlledPartitionMovementNativeHostEndpoints.OwnerCount
            || !Path.IsPathFullyQualified(input.Root)
            || !StringComparer.Ordinal.Equals(Path.GetFullPath(input.Root), Path.GetFullPath(originalRoot))
            || !Path.GetFileName(input.Root).StartsWith(RootPrefix, StringComparison.Ordinal))
        {
            throw Invalid();
        }
        var origins = ImmutableArray.CreateBuilder<string>();
        var silos = ImmutableArray.CreateBuilder<IPEndPoint>();
        foreach (var arguments in input.Arguments)
        {
            if (arguments.IsDefaultOrEmpty)
            {
                throw Invalid();
            }
            var origin = Require(arguments, UrlPrefix);
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttp || !uri.IsLoopback
                || uri.Host != IPAddress.Loopback.ToString() || uri.Port <= IPEndPoint.MinPort
                || uri.UserInfo.Length != EmptyUriComponentLength || uri.Query.Length != EmptyUriComponentLength
                || uri.Fragment.Length != EmptyUriComponentLength
                || uri.AbsolutePath != OriginRootPath)
            {
                throw Invalid();
            }
            var address = Read(arguments, nameof(NodeOptions.SiloAddress));
            var port = Read(arguments, nameof(NodeOptions.SiloPort));
            if (address != IPAddress.Loopback.ToString()
                || !int.TryParse(port, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                || number <= IPEndPoint.MinPort || number > IPEndPoint.MaxPort)
            {
                throw Invalid();
            }
            var expected = Path.Combine(input.Root, NodeDirectoryPrefix
                + origins.Count.ToString(CultureInfo.InvariantCulture));
            if (!StringComparer.Ordinal.Equals(Read(arguments, nameof(NodeOptions.DataDirectory)), expected))
            {
                throw Invalid();
            }
            origins.Add(origin);
            silos.Add(new(IPAddress.Loopback, number));
        }
        if (origins.Distinct(StringComparer.Ordinal).Count() != origins.Count
            || silos.Select(endpoint => endpoint.Port).Distinct().Count() != silos.Count
            || input.PrimarySiloPort != silos[FirstNode].Port)
        {
            throw Invalid();
        }
        return (origins.ToImmutable(), silos.ToImmutable());
    }

    internal static string Read(ImmutableArray<string> arguments, string key)
        => Require(arguments, NodePrefix + key + Assignment);

    private static string Require(ImmutableArray<string> arguments, string prefix)
    {
        var selected = arguments.Where(value => value is not null
            && value.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
        if (selected.Length != SingleArgumentCount || selected[FirstNode].Length == prefix.Length)
        {
            throw Invalid();
        }
        return selected[FirstNode][prefix.Length..];
    }

    internal static Exception Invalid()
        => new InvalidOperationException(ControlledPartitionMovementProcessProtocol.Invalid);
}
