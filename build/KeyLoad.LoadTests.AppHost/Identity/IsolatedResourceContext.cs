using System.Globalization;
using KeyLoad.Comparisons;

namespace KeyLoad.LoadTests.AppHost.BenchmarkComparisons;

internal sealed record IsolatedResourceContext(IDistributedApplicationBuilder Builder,
    BenchmarkSessionSelection Selection, LoadTestClientSettings Client, string Root)
{
    private const string RuntimeDirectoryCore = "execution";
    private const string RuntimePrefix = "Benchmarks__Execution__";
    private const string EndpointsPrefix = RuntimePrefix + "Endpoints__";
    private const string ImageSetting = RuntimePrefix + "Image";

    internal string DataDirectory(string name)
    {
        var path = Path.Combine(Root, RuntimeDirectoryCore, name);
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

    internal void BindSetting(string name, string value) => Client.Set(RuntimePrefix + name, value);

    internal void BindSetting(string name, IResourceBuilder<ParameterResource> value)
        => Client.Set(RuntimePrefix + name, value.Resource);

    internal void BindSetting(string name, ReferenceExpression value) => Client.Set(RuntimePrefix + name, value);

    internal void BindImage(string reference) => Client.Set(ImageSetting, reference);
}
