using KeyLoad;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.AppHost.Hosting;

/// <summary>Build-time application mode and container process settings bound by the AppHost.</summary>
[ConfigurationOptions]
internal sealed class AppHostStartupOptions
{
    private const int DefaultLookupSeconds = 5;
    private const int MaximumLookupSeconds = 60;
    private const int DefaultLookupCharacters = 32;
    private const int MaximumLookupCharacters = 4_096;
    private const int MinimumLookupCharacters = 1;
    internal const string ValidationMessage = "Container identity lookup settings exceed supported bounds.";
    [ConfigurationKeyName("KeyLoad:ContainerUser")]
    public string? ContainerUser { get; set; }
    [ConfigurationKeyName("KeyLoad:ContainerIdentityLookupTimeout")]
    public TimeSpan ContainerIdentityLookupTimeout { get; set; } = TimeSpan.FromSeconds(DefaultLookupSeconds);
    [ConfigurationKeyName("KeyLoad:ContainerIdentityOutputCharacters")]
    public int ContainerIdentityOutputCharacters { get; set; } = DefaultLookupCharacters;
    [ConfigurationKeyName("Benchmarks:Enabled")]
    public bool BenchmarkMode { get; set; }
    [ConfigurationKeyName("Benchmarks:Profile")]
    public string BenchmarkProfile { get; set; } = global::AppHostConfiguration.GeneralBenchmarkProfile;
    [ConfigurationKeyName("Benchmarks:DataRoot")]
    public string? BenchmarkRoot { get; set; }
    [ConfigurationKeyName("KeyLoad:DataRoot")]
    public string? DataRoot { get; set; }
    [ConfigurationKeyName("KeyLoad:Ephemeral")]
    public bool? Ephemeral { get; set; }

    internal bool IsValid() => ContainerIdentityLookupTimeout > TimeSpan.Zero
        && ContainerIdentityLookupTimeout <= TimeSpan.FromSeconds(MaximumLookupSeconds)
        && ContainerIdentityOutputCharacters is >= MinimumLookupCharacters and <= MaximumLookupCharacters;
}
