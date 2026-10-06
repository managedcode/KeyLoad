using KeyLoad;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.AppHost.Hosting;

/// <summary>Build-time application mode and container process settings bound by the AppHost.</summary>
[ConfigurationOptions]
internal sealed class AppHostStartupOptions
{
    private const string ContainerUserKey = "KeyLoad:ContainerUser";
    private const string ContainerIdentityLookupTimeoutKey = "KeyLoad:ContainerIdentityLookupTimeout";
    private const string ContainerIdentityOutputCharactersKey = "KeyLoad:ContainerIdentityOutputCharacters";
    private const string BenchmarkModeKey = "Benchmarks:Enabled";
    private const string BenchmarkProfileKey = "Benchmarks:Profile";
    private const string BenchmarkOutputKey = "Benchmarks:Output";
    private const string BenchmarkRootKey = "Benchmarks:DataRoot";
    private const string DataRootKey = "KeyLoad:DataRoot";
    private const string EphemeralKey = "KeyLoad:Ephemeral";
    private const int DefaultLookupSeconds = 5;
    private const int MaximumLookupSeconds = 60;
    private const int DefaultLookupCharacters = 32;
    private const int MaximumLookupCharacters = 4_096;
    private const int MinimumLookupCharacters = 1;
    internal const string ValidationMessage = "Container identity lookup settings exceed supported bounds.";
    [ConfigurationKeyName(ContainerUserKey)]
    public string? ContainerUser { get; set; }
    [ConfigurationKeyName(ContainerIdentityLookupTimeoutKey)]
    public TimeSpan ContainerIdentityLookupTimeout { get; set; } = TimeSpan.FromSeconds(DefaultLookupSeconds);
    [ConfigurationKeyName(ContainerIdentityOutputCharactersKey)]
    public int ContainerIdentityOutputCharacters { get; set; } = DefaultLookupCharacters;
    [ConfigurationKeyName(BenchmarkModeKey)]
    public bool BenchmarkMode { get; set; }
    [ConfigurationKeyName(BenchmarkProfileKey)]
    public string BenchmarkProfile { get; set; } = global::AppHostConfiguration.GeneralBenchmarkProfile;
    [ConfigurationKeyName(BenchmarkOutputKey)]
    public string? BenchmarkOutput { get; set; }
    [ConfigurationKeyName(BenchmarkRootKey)]
    public string? BenchmarkRoot { get; set; }
    [ConfigurationKeyName(DataRootKey)]
    public string? DataRoot { get; set; }
    [ConfigurationKeyName(EphemeralKey)]
    public bool? Ephemeral { get; set; }

    internal bool IsValid() => ContainerIdentityLookupTimeout > TimeSpan.Zero
        && ContainerIdentityLookupTimeout.Ticks <= MaximumLookupSeconds * TimeSpan.TicksPerSecond
        && ContainerIdentityOutputCharacters is >= MinimumLookupCharacters and <= MaximumLookupCharacters;
}
