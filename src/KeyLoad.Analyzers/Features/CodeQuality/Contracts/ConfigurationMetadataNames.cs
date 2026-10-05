namespace KeyLoad.Analyzers.Features.CodeQuality;

internal static class ConfigurationMetadataNames
{
    internal const string OwnerAssembly = "KeyLoad.Abstractions";
    internal const string OptionsOwner = "KeyLoad.ConfigurationOptionsAttribute";
    internal const string BindingOwner = "KeyLoad.ConfigurationBindingAttribute";
    internal const string Options = "Microsoft.Extensions.Options.IOptions`1";
    internal const string Configuration = "Microsoft.Extensions.Configuration.IConfiguration";
    internal const string ConfigurationBinder = "Microsoft.Extensions.Configuration.ConfigurationBinder";
    internal const string ConfigurationExtensions = "Microsoft.Extensions.Configuration.ConfigurationExtensions";
    internal const string Environment = "System.Environment";
    internal const string GetEnvironmentVariable = "GetEnvironmentVariable";
    internal const string GetEnvironmentVariables = "GetEnvironmentVariables";
    internal const string GetCommandLineArgs = "GetCommandLineArgs";
    internal const string Value = "Value";
    internal const string Timeout = "System.Threading.Timeout";
    internal const string Infinite = "Infinite";
    internal const string InfiniteTimeSpan = "InfiniteTimeSpan";
    internal const string Zero = "Zero";
    internal const string MinValue = "MinValue";
    internal const string MaxValue = "MaxValue";
}
