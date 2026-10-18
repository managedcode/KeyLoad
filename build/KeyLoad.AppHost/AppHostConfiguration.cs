using KeyLoad.AppHost.Hosting;

internal sealed record AppHostConfiguration(string DataRoot, bool Ephemeral)
{
    private const string DefaultDataRoot = "../../data/cluster";
    internal const string AdminParameter = "admin-key";

    internal static AppHostConfiguration Read(IDistributedApplicationBuilder builder)
    {
        var options = AppHostOptionsRegistration.Get(builder).Startup.Value;
        var dataRoot = Path.GetFullPath(options.DataRoot ?? Path.Combine(builder.AppHostDirectory, DefaultDataRoot));
        return new(dataRoot, options.Ephemeral ?? false);
    }
}
