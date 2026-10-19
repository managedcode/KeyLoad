using System.Globalization;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>A single closed child authority; only declared environment names enter Docker arguments.</summary>
internal sealed record MongoReadinessChecksChild(string Name, string Image, string Network,
    string Module, string Script, IReadOnlyDictionary<string, string> Labels, IReadOnlyDictionary<string, string> Environment)
{
    private const string Run = "run", NameArgument = "--name", NetworkArgument = "--network", Pull = "--pull", Never = "never";
    private const string ReadOnly = "--read-only", CapDrop = "--cap-drop", All = "ALL", SecurityOption = "--security-opt";
    private const string NoNewPrivileges = "no-new-privileges", Mount = "--mount", EnvironmentArgument = "--env", Label = "--label";
    private const string EntryPoint = "--entrypoint", Shell = "mongosh", Quiet = "--quiet", NoDatabase = "--nodb";
    private const string NoRc = "--norc", UserArgument = "--user", User = "0:0", Tmpfs = "--tmpfs";
    private const string RuntimeState = "/root/.mongodb/mongosh:rw,nosuid,nodev,noexec,size=16777216,mode=0700,uid=0,gid=0";
    private const string BindPrefix = "type=bind,source=", BindTarget = ",target=", BindReadOnly = ",readonly";
    private const char LabelSeparator = '=', HostSeparator = ',';

    internal static MongoReadinessChecksChild Create(string image, string network, string module, string password, int count)
    {
        var fixture = Guid.NewGuid().ToString(MongoReadinessChecksProtocol.GuidFormat);
        var source = Read(MongoReadinessChecksProtocol.SourceEnvironment);
        var run = Read(MongoReadinessChecksProtocol.RunEnvironment);
        var attempt = Read(MongoReadinessChecksProtocol.AttemptEnvironment);
        MongoReadinessChecksProtocol.Require(source.Length == MongoReadinessChecksProtocol.SourceCharacters
            && source.All(static item => char.IsAsciiHexDigitLower(item)) && Positive(run) && Positive(attempt));
        var labels = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [MongoReadinessChecksProtocol.SourceLabel] = source,
            [MongoReadinessChecksProtocol.RunLabel] = run,
            [MongoReadinessChecksProtocol.AttemptLabel] = attempt,
            [MongoReadinessChecksProtocol.FixtureLabel] = fixture
        };
        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [MongoReadinessChecksProtocol.UserEnvironment] = MongoReadinessChecksProtocol.Username,
            [MongoReadinessChecksProtocol.PasswordEnvironment] = password,
            [MongoReadinessChecksProtocol.MembersEnvironment] = string.Join(HostSeparator,
                Enumerable.Range(0, count).Select(index => MongoReadinessChecksProtocol.Node(index) + MongoReadinessChecksProtocol.Port))
        };
        return new(MongoReadinessChecksProtocol.ChildPrefix + fixture, image, network, module,
            MongoReadinessChecksProtocol.Source(MongoReadinessChecksProtocol.FixtureFile), labels, environment);
    }

    internal string[] Arguments()
    {
        var arguments = new List<string> { Run, NameArgument, Name, NetworkArgument, Network, Pull, Never,
            ReadOnly, UserArgument, User, Tmpfs, RuntimeState, CapDrop, All, SecurityOption, NoNewPrivileges, EntryPoint, Shell,
            Mount, Bind(Module, MongoReadinessChecksProtocol.ModuleTarget),
            Mount, Bind(Script, MongoReadinessChecksProtocol.FixtureTarget) };
        foreach (var variable in Environment.Keys)
        {
            arguments.Add(EnvironmentArgument);
            arguments.Add(variable);
        }
        foreach (var label in Labels)
        {
            arguments.Add(Label);
            arguments.Add(label.Key + LabelSeparator + label.Value);
        }
        arguments.AddRange([Image, Quiet, NoDatabase, NoRc, MongoReadinessChecksProtocol.FixtureTarget]);
        return [.. arguments];
    }

    private static string Read(string name) => System.Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException(MongoReadinessChecksProtocol.Failure);

    private static bool Positive(string value)
        => value.All(char.IsAsciiDigit) && long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed > 0;

    private static string Bind(string source, string target)
    {
        MongoReadinessChecksProtocol.Require(Path.IsPathFullyQualified(source) && File.Exists(source)
            && !source.Contains(HostSeparator, StringComparison.Ordinal));
        return BindPrefix + source + BindTarget + target + BindReadOnly;
    }
}
