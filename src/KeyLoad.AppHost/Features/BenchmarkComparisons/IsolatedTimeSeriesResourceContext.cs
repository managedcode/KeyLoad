using System.Globalization;

namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal sealed class IsolatedTimeSeriesResourceContext
{
    private const int MinimumNodes = 1;
    private const int MaximumNodes = 3;
    private const int FirstEndpointIndex = 0;
    private const char NameSeparator = '-';
    private const string Invalid = "IsolatedTimeSeriesResourceContextInvalid";
    private const string NativeDirectory = "native";
    private const string NativePrefix = "Benchmarks__Native__";
    private const string EndpointPrefix = NativePrefix + "Endpoints__";
    private const string ImageSetting = NativePrefix + "Image";

    internal IsolatedTimeSeriesResourceContext(IDistributedApplicationBuilder builder, int nodeCount,
        IResourceBuilder<ContainerResource> runner, string root)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(runner);
        if (nodeCount is < MinimumNodes or > MaximumNodes || string.IsNullOrWhiteSpace(root))
        {
            throw new InvalidOperationException(Invalid);
        }
        Builder = builder;
        NodeCount = nodeCount;
        Runner = runner;
        Root = Path.GetFullPath(root);
    }

    internal IDistributedApplicationBuilder Builder { get; }
    internal int NodeCount { get; }
    internal IResourceBuilder<ContainerResource> Runner { get; }
    internal string Root { get; }

    internal string DataDirectory(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Any(static item => !char.IsAsciiLetterOrDigit(item) && item != NameSeparator))
        {
            throw new InvalidOperationException(Invalid);
        }
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
        if (index < FirstEndpointIndex || index >= NodeCount)
        {
            throw new InvalidOperationException(Invalid);
        }
        Runner.WithEnvironment(EndpointPrefix + index.ToString(CultureInfo.InvariantCulture), node.GetEndpoint(endpointName));
        Runner.WaitFor(node);
    }

    internal void BindSetting(string name, string value) => Runner.WithEnvironment(NativePrefix + name, value);
    internal void BindSetting(string name, IResourceBuilder<ParameterResource> value) => Runner.WithEnvironment(NativePrefix + name, value);
    internal void BindSetting(string name, ReferenceExpression value) => Runner.WithEnvironment(NativePrefix + name, value);
    internal void BindImage(string reference) => Runner.WithEnvironment(ImageSetting, reference);
}
