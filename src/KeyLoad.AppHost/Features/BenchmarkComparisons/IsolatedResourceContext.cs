using System.Globalization;
using KeyLoad.Comparisons;

namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal sealed record IsolatedResourceContext(IDistributedApplicationBuilder Builder,
    ComparisonWorkerSelection Selection, IResourceBuilder<ContainerResource> Runner, string Root)
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

    internal void BindEndpoint(int index, IResourceBuilder<ContainerResource> node, string endpointName)
    {
        Runner.WithEnvironment(EndpointsPrefix + index.ToString(CultureInfo.InvariantCulture), node.GetEndpoint(endpointName));
        Runner.WaitFor(node);
    }

    internal void BindSetting(string name, string value) => Runner.WithEnvironment(NativePrefix + name, value);

    internal void BindSetting(string name, IResourceBuilder<ParameterResource> value)
        => Runner.WithEnvironment(NativePrefix + name, value);

    internal void BindSetting(string name, ReferenceExpression value) => Runner.WithEnvironment(NativePrefix + name, value);

    internal void BindImage(string reference) => Runner.WithEnvironment(ImageSetting, reference);
}
