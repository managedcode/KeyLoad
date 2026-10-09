using System.Globalization;
using System.Text;
using Microsoft.Extensions.Options;
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
    private const string SlotRetentionFormat = "max_slot_wal_keep_size={0}MB";
    private static readonly CompositeFormat SlotRetentionCompositeFormat = CompositeFormat.Parse(SlotRetentionFormat);
    private const string MaximumConnectionsPrefix = "max_connections=";
    private const string MissingBootstrap = "IsolatedPostgresBootstrapMissing";

    internal static (string Entry, string Init) FindScripts(IsolatedResourceContext context) => FindScripts(context.Builder);

    internal static (string Entry, string Init) FindScripts(IDistributedApplicationBuilder builder)
    {
        var source = Path.Combine(builder.AppHostDirectory, SourceDirectory);
        var entry = Path.Combine(source, EntryName);
        var init = Path.Combine(source, InitName);
        if (!File.Exists(entry) || !File.Exists(init))
        {
            throw new InvalidOperationException(MissingBootstrap);
        }
        return (entry, init);
    }

    internal static void Configure<T>(IResourceBuilder<T> node, string directory, (string Entry, string Init) scripts,
        IResourceBuilder<ParameterResource> password, string? standby, int standbyCount, IOptions<BenchmarkDeploymentOptions> deploymentOptions, string primaryName = Primary, bool documentWorkload = false) where T : ContainerResource
    {
        var slotRetention = string.Format(CultureInfo.InvariantCulture, SlotRetentionCompositeFormat, deploymentOptions.Value.PostgresSlotWalRetentionMegabytes);
        node.WithBindMount(directory, Data).WithBindMount(scripts.Entry, EntryTarget, isReadOnly: true)
            .WithEnvironment(PgDataEnvironment, PgData).WithEnvironment(Password, password).WithEntrypoint(Shell)
            .WithArgs(arguments =>
            {
                arguments.Args.Clear();
                foreach (var argument in new[] { EntryTarget, Postgres, Configuration, Fsync, Configuration, Commit,
                    Configuration, slotRetention })
                {
                    arguments.Args.Add(argument);
                }
                if (documentWorkload)
                {
                    arguments.Args.Add(Configuration);
                    arguments.Args.Add(MaximumConnectionsPrefix + deploymentOptions.Value.DocumentPostgresMaximumConnections
                        .ToString(CultureInfo.InvariantCulture));
                }
            });
        if (standby is null)
        {
            node.WithBindMount(scripts.Init, InitTarget, isReadOnly: true)
                .WithEnvironment(StandbyCount, standbyCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        else
        {
            node.WithEnvironment(PrimaryEnvironment, primaryName).WithEnvironment(ApplicationName, StandbyPrefix + standby);
        }
    }
}
