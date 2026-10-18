using System.Globalization;
using KeyLoad.Comparisons;

namespace KeyLoad.LoadTests.AppHost.Features.BenchmarkComparisons;

internal sealed record IsolatedResourceContext(IDistributedApplicationBuilder Builder,
    BenchmarkSessionSelection Selection, LoadTestClientSettings Client, string Root)
{
    private const string NativeDirectory = "native";
    private const string NativePrefix = "Benchmarks__Native__";
    private const string EndpointsPrefix = NativePrefix + "Endpoints__";
    private const string ImageSetting = NativePrefix + "Image";

    internal string DataDirectory(string name)
    {
        var path = Path.Combine(Root, NativeDirectory, name);
        Directory.CreateDirectory(path);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        return path;
    }

    internal void BindEndpoint<T>(int index, IResourceBuilder<T> node, string endpointName)
        where T : IResourceWithEndpoints
    {
        Client.Set(EndpointsPrefix + index.ToString(CultureInfo.InvariantCulture), node.GetEndpoint(endpointName));
        Client.Resources.Add(node.Resource.Name);
    }

    internal void BindSetting(string name, string value) => Client.Set(NativePrefix + name, value);

    internal void BindSetting(string name, IResourceBuilder<ParameterResource> value)
        => Client.Set(NativePrefix + name, value.Resource);

    internal void BindSetting(string name, ReferenceExpression value) => Client.Set(NativePrefix + name, value);

    internal void BindImage(string reference) => Client.Set(ImageSetting, reference);
}
