namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal static class IsolatedPostgresBootstrap
{
    private const string SourceDirectory = "Features/BenchmarkComparisons";
    private const string EntryName = "IsolatedPostgresEntry.sh";
    private const string InitName = "IsolatedPostgresReplication.sh";
    private const string EntryTarget = "/bootstrap/isolated-postgres.sh";
    private const string InitTarget = "/docker-entrypoint-initdb.d/isolated-replication.sh";
    private const string Data = "/var/lib/postgresql";
    private const string PgData = "/var/lib/postgresql/18/docker";
    private const string PgDataEnvironment = "PGDATA";
    private const string Password = "PGPASSWORD";
    private const string ApplicationName = "PGAPPNAME";
    private const string StandbyPrefix = "benchmark_standby";
    private const string PrimaryEnvironment = "KEYLOAD_POSTGRES_PRIMARY";
    private const string Primary = "isolated-postgres-1";
    private const string StandbyCount = "KEYLOAD_POSTGRES_STANDBYS";
    private const string Shell = "/bin/sh";
    private const string Postgres = "postgres";
    private const string Configuration = "-c";
    private const string Fsync = "fsync=on";
    private const string Commit = "synchronous_commit=on";
    private const string SlotRetention = "max_slot_wal_keep_size=512MB";
    private const string MissingBootstrap = "IsolatedPostgresBootstrapMissing";

    internal static (string Entry, string Init) FindScripts(IsolatedResourceContext context)
    {
        var source = Path.Combine(context.Builder.AppHostDirectory, SourceDirectory);
        var entry = Path.Combine(source, EntryName);
        var init = Path.Combine(source, InitName);
        if (!File.Exists(entry) || !File.Exists(init))
        {
            throw new InvalidOperationException(MissingBootstrap);
        }
        return (entry, init);
    }

    internal static void Configure<T>(IResourceBuilder<T> node, string directory, (string Entry, string Init) scripts,
        IResourceBuilder<ParameterResource> password, string? standby, int standbyCount) where T : ContainerResource
    {
        node.WithBindMount(directory, Data).WithBindMount(scripts.Entry, EntryTarget, isReadOnly: true)
            .WithEnvironment(PgDataEnvironment, PgData).WithEnvironment(Password, password).WithEntrypoint(Shell)
            .WithArgs(static arguments =>
            {
                arguments.Args.Clear();
                foreach (var argument in new[] { EntryTarget, Postgres, Configuration, Fsync, Configuration, Commit,
                    Configuration, SlotRetention })
                {
                    arguments.Args.Add(argument);
                }
            });
        if (standby is null)
        {
            node.WithBindMount(scripts.Init, InitTarget, isReadOnly: true)
                .WithEnvironment(StandbyCount, standbyCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        else
        {
            node.WithEnvironment(PrimaryEnvironment, Primary).WithEnvironment(ApplicationName, StandbyPrefix + standby);
        }
    }
}
