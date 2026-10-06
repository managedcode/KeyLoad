using KeyLoad.AppHost.Hosting;
using System.Globalization;
using KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal static class IsolatedTimeSeriesBenchmarkResources
{
    private const string EnabledSetting = "Benchmarks:Enabled";
    private const string RootSetting = "Benchmarks:DataRoot";
    private const string OutputSetting = "Benchmarks:Output";
    private const string TemporaryPrefix = "keyload-timeseries-intensive-";
    private const string ReportsDirectory = "reports";
    private const string NativeDirectory = "native";
    private const string RouteSetting = "Benchmarks:Profile";
    private const string RouteValue = "timeseries-intensive";
    private const string CellIdSetting = "Benchmarks:TimeSeries:CellId";
    private const string ContractHashSetting = "Benchmarks:TimeSeries:ContractSha256";
    private const string StorageSetting = "Benchmarks__Storage";
    private const string Storage = "Fresh TimeSeries cell-owned native directories; no shared database";
    private const string InvalidSelection = "IsolatedTimeSeriesBenchmarkSelectionInvalid";
    private const string InvalidPath = "IsolatedTimeSeriesBenchmarkPathInvalid";
    private const string ExistingNativePath = "IsolatedTimeSeriesNativePathAlreadyExists";
    private const string TimeSeriesPrefix = "Benchmarks:TimeSeries:";
    private const string TargetSetting = TimeSeriesPrefix + "Target";
    private const string NodeCountSetting = TimeSeriesPrefix + "NodeCount";
    private const string PhaseSetting = TimeSeriesPrefix + "Phase";
    private const string ScenarioSetting = TimeSeriesPrefix + "Scenario";
    private const string EvidenceProfileSetting = TimeSeriesPrefix + "EvidenceProfile";
    private const string GuidFormat = "N";
    private const string SettingSeparator = ":";
    private const string EnvironmentSeparator = "__";
    private const UnixFileMode PrivateDirectoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode SharedDirectoryModes = UnixFileMode.GroupRead | UnixFileMode.GroupWrite
        | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
    private const int FirstMatchIndex = 0;
    private const int ExpectedMatchCount = 1;
    private const int ExtraMatchAllowance = 1;

    internal static void Add(IDistributedApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var selection = TimeSeriesIntensiveSelection.Read(builder.Configuration);
        RequireEnabled(builder);
        var contract = TimeSeriesIntensiveFamilyContract.Current;
        var cell = FindCell(selection, contract);
        RequireOptionalIdentity(builder, cell, contract.ContractSha256);
        var root = ResolvePath(AppHostOptionsRegistration.Get(builder).Startup.Value.BenchmarkRoot, DefaultRoot());
        var output = ResolvePath(AppHostOptionsRegistration.Get(builder).Startup.Value.BenchmarkOutput, Path.Combine(root, ReportsDirectory));
        RequireFreshNativePath(root);
        ValidatePrivateDirectoryIfExisting(root);
        ValidatePrivateDirectoryIfExisting(output);
        EnsurePrivateDirectory(root);
        EnsurePrivateDirectory(output);
        var runner = BenchmarkRunnerContainer.Create(builder, output);
        BindSelection(runner, selection);
        BindIdentity(runner, cell, contract.ContractSha256);
        runner.WithEnvironment(StorageSetting, Storage);
        var context = new IsolatedTimeSeriesResourceContext(builder, selection.NodeCount, runner, root);
        AddSelectedNativeResources(selection.Target, context);
    }

    private static void RequireEnabled(IDistributedApplicationBuilder builder)
    {
        if (!AppHostOptionsRegistration.Get(builder).Startup.Value.BenchmarkMode)
        {
            throw new InvalidOperationException(InvalidSelection);
        }
    }

    private static TimeSeriesIntensiveFamilyCell FindCell(TimeSeriesIntensiveSelection selection,
        TimeSeriesIntensiveFamilyContract contract)
    {
        var cells = TimeSeriesIntensiveFamilyPlan.Create(contract);
        var matching = cells.Preflight.Concat(cells.Intensive)
            .Where(cell => cell.Selection == selection).Take(ExpectedMatchCount + ExtraMatchAllowance).ToArray();
        if (matching.Length != ExpectedMatchCount)
        {
            throw new InvalidOperationException(InvalidSelection);
        }
        return matching[FirstMatchIndex];
    }

    private static void RequireOptionalIdentity(IDistributedApplicationBuilder builder,
        TimeSeriesIntensiveFamilyCell cell, string contractSha)
    {
        var configuredCell = AppHostOptionsRegistration.Get(builder).BenchmarkRelay.Value.TimeSeriesCellId;
        var configuredHash = AppHostOptionsRegistration.Get(builder).BenchmarkRelay.Value.TimeSeriesContractSha256;
        if ((configuredCell is not null && configuredCell != cell.Id)
            || (configuredHash is not null && configuredHash != contractSha))
        {
            throw new InvalidOperationException(InvalidSelection);
        }
    }

    private static string DefaultRoot()
        => Path.Combine(Path.GetTempPath(), TemporaryPrefix + Guid.NewGuid().ToString(GuidFormat, CultureInfo.InvariantCulture));

    private static string ResolvePath(string? configured, string fallback)
    {
        if (configured is not null && !Path.IsPathFullyQualified(configured))
        {
            throw new InvalidOperationException(InvalidPath);
        }
        return Path.GetFullPath(configured ?? fallback);
    }

    private static void RequireFreshNativePath(string root)
    {
        var native = Path.Combine(root, NativeDirectory);
        try
        {
            _ = File.GetAttributes(native);
        }
        catch (FileNotFoundException)
        {
            return;
        }
        catch (DirectoryNotFoundException)
        {
            return;
        }
        throw new InvalidOperationException(ExistingNativePath);
    }

    private static void EnsurePrivateDirectory(string path)
    {
        var existed = Directory.Exists(path);
        if (!existed && File.Exists(path))
        {
            throw new InvalidOperationException(InvalidPath);
        }
        Directory.CreateDirectory(path);
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        if (!existed)
        {
            File.SetUnixFileMode(path, PrivateDirectoryMode);
        }
    }

    private static void ValidatePrivateDirectoryIfExisting(string path)
    {
        const int EmptyValue = 0;

        if (!Directory.Exists(path))
        {
            if (File.Exists(path))
            {
                throw new InvalidOperationException(InvalidPath);
            }
            return;
        }
        if (!OperatingSystem.IsWindows())
        {
            var mode = File.GetUnixFileMode(path);
            if ((mode & SharedDirectoryModes) != EmptyValue || (mode & PrivateDirectoryMode) != PrivateDirectoryMode)
            {
                throw new InvalidOperationException(InvalidPath);
            }
        }
    }

    private static void BindSelection(IResourceBuilder<ContainerResource> runner,
        TimeSeriesIntensiveSelection selection)
    {
        Bind(runner, RouteSetting, RouteValue);
        Bind(runner, TargetSetting, selection.Target.ToString());
        Bind(runner, NodeCountSetting, selection.NodeCount.ToString(CultureInfo.InvariantCulture));
        Bind(runner, PhaseSetting, selection.Phase.ToString());
        Bind(runner, EvidenceProfileSetting, selection.EvidenceProfile);
        if (selection.Scenario is { } scenario)
        {
            Bind(runner, ScenarioSetting, scenario.ToString());
        }
    }

    private static void BindIdentity(IResourceBuilder<ContainerResource> runner,
        TimeSeriesIntensiveFamilyCell cell, string contractSha)
    {
        Bind(runner, CellIdSetting, cell.Id);
        Bind(runner, ContractHashSetting, contractSha);
    }

    private static void Bind(IResourceBuilder<ContainerResource> runner, string setting, string value)
        => runner.WithEnvironment(setting.Replace(SettingSeparator, EnvironmentSeparator, StringComparison.Ordinal), value);

    private static void AddSelectedNativeResources(TimeSeriesIntensiveTargetKind target,
        IsolatedTimeSeriesResourceContext context)
    {
        switch (target)
        {
            case TimeSeriesIntensiveTargetKind.KeyLoad:
                IsolatedTimeSeriesKeyLoadResources.Add(context);
                break;
            case TimeSeriesIntensiveTargetKind.TimescaleDB:
                IsolatedTimeSeriesTimescaleResources.Add(context);
                break;
            default:
                throw new InvalidOperationException(InvalidSelection);
        }
    }
}
