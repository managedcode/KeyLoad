using Microsoft.Extensions.Configuration;

namespace KeyLoad.AppHost.Hosting;

/// <summary>Owns the execution KeyLoad process data root and session lifetime.</summary>
[ConfigurationOptions]
internal sealed class AppHostStartupOptions
{
    private const string DataRootKey = "KeyLoad:DataRoot";
    private const string EphemeralKey = "KeyLoad:Ephemeral";
    internal const string ValidationMessage = "The KeyLoad data root must be an absolute path when configured.";

    [ConfigurationKeyName(DataRootKey)]
    public string? DataRoot
    {
        get; set;
    }

    [ConfigurationKeyName(EphemeralKey)]
    public bool? Ephemeral
    {
        get; set;
    }

    internal bool IsValid() => DataRoot is null || Path.IsPathFullyQualified(DataRoot);
}
