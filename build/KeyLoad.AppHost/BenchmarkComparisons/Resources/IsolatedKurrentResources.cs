using System.Globalization;

namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal static class IsolatedKurrentResources
{
    private const string Target = "KurrentDB";
    private const string NodePrefix = "isolated-kurrent-";
    private const string Image = "kurrentplatform/kurrentdb";
    private const string Tag = "26.1.2";
    private const string Http = "http";
    private const string Data = "/var/lib/kurrentdb";
    private const string VolumeSuffix = "-data-";
    private const string VolumePrefix = "keyload-";
    private const string HealthPath = "/health/live?liveCode=200";
    private const string GuidFormat = "N";
    private const string Registry = "docker.io/";
    private const string TagSeparator = ":";
    private const string DigestSeparator = "@";
    private const int DigestPrefixLength = 7;
    private const int Port = 2113;
    private const string Connection = "ConnectionString";
    private const string NodePort = ":2113";
    private const string ConnectionPrefix = "esdb://";
    private const string ConnectionOptions = "?tls=false&nodePreference=leader";
    private const string InvalidSelection = "IsolatedKurrentSelectionInvalid";

    internal static void Add(IsolatedResourceContext context)
    {
        const int StartValue = 1;
        const int IndexInitialValue = 0;
        const char SeparatorCharacter = ',';

        ArgumentNullException.ThrowIfNull(context);
        context.Selection.Validate();
        if (context.Selection.Target != Target)
        {
            throw new InvalidOperationException(InvalidSelection);
        }
        var cell = Guid.NewGuid().ToString(GuidFormat);
        var names = Enumerable.Range(StartValue, context.Selection.NodeCount)
            .Select(index => NodePrefix + index.ToString(CultureInfo.InvariantCulture)).ToArray();
        for (var index = IndexInitialValue; index < names.Length; index++)
        {
            var name = names[index];
            var node = context.Builder.AddContainer(name, Image, Tag).WithImageSHA256(BenchmarkResources.KurrentDigest[DigestPrefixLength..])
                .WithContainerNetworkAlias(name).WithVolume(VolumePrefix + name + VolumeSuffix + cell, Data)
                .WithHttpEndpoint(targetPort: Port, name: Http).WithHttpHealthCheck(HealthPath);
            IsolatedKurrentSettings.Configure(node, name, names);
            context.BindEndpoint(index, node, Http);
        }
        context.BindSetting(Connection, ConnectionPrefix +
            string.Join(SeparatorCharacter, names.Select(name => IsolatedKurrentSettings.NativeHost(name) + NodePort)) + ConnectionOptions);
        context.BindImage(Registry + Image + TagSeparator + Tag + DigestSeparator + BenchmarkResources.KurrentDigest);
    }
}
