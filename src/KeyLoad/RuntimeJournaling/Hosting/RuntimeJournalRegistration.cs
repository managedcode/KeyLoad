#pragma warning disable ORLEANSEXP005
using KeyLoad.Clustering;
using KeyLoad.Core;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.Options;
using Orleans.DurableJobs;
using Orleans.Journaling;
using Orleans.Serialization;

namespace KeyLoad.Server;

[ConfigurationBinding]
internal static class RuntimeJournalRegistration
{
    internal const string ProviderName = "keyload-cluster-runtime-journal";
    private const string BinaryFormat = "orleans-binary";

    internal static void Register(ISiloBuilder silo)
    {
        var services = silo.Services;
        services.AddSingleton<RuntimeJournalAdmission>();
        services.AddSingleton<RuntimeJournalClient>(CreateClient);
        silo.AddJournalStorage(ProviderName, provider => new RuntimeJournalStorageProvider(
            provider.GetRequiredService<RuntimeJournalClient>(), provider.GetRequiredService<IOptions<RuntimeJournalOptions>>(),
            provider.GetRequiredService<IOptions<GrainRoutingOptions>>(),
            provider.GetRequiredService<IOptions<JournaledStateManagerOptions>>()));
        // A grain-scoped IDurableStateManager (the time-series chunk and ANN projection coordinators) resolves the unkeyed
        // provider, which Orleans registers only for its "Default" name; KeyLoad has exactly this one journal store.
        services.AddSingleton<IJournalStorageProvider>(provider =>
            provider.GetRequiredKeyedService<IJournalStorageProvider>(ProviderName));
        silo.Configure<JournaledStateManagerOptions>(options => options.JournalFormatKey = BinaryFormat);
        services.AddOptions<DurableJobsOptions>().Configure<IOptions<RuntimeDurableJobOptions>, TimeProvider>(
            (options, settings, clock) => Configure(options, settings.Value, clock));
        silo.UseJournaledDurableJobs();
        RequestDrainRegistration.Register(services);
        JobGraphRegistration.Extend(services);
    }

    private static RuntimeJournalClient CreateClient(IServiceProvider provider)
    {
        var codec = new Lazy<GrainRequestCodec>(provider.GetRequiredService<GrainRequestCodec>);
        return new RuntimeJournalClient(provider.GetRequiredService<IGrainFactory>(),
            () => codec.Value, provider.GetRequiredService<IOptions<DatabaseLimits>>(), provider,
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>>(),
            provider.GetRequiredService<IOptions<GrainRoutingOptions>>(),
            provider.GetRequiredService<IOptions<RuntimeJournalOptions>>(),
            provider.GetRequiredService<RuntimeJournalAdmission>());
    }

    private static void Configure(DurableJobsOptions execution, RuntimeDurableJobOptions settings, TimeProvider clock)
    {
        if (!settings.IsValid())
        {
            throw new InvalidOperationException(RuntimeDurableJobOptions.ValidationMessage);
        }
        execution.ActiveProviderName = ProviderName;
        execution.ShardDuration = settings.ShardDuration;
        execution.ShardActivationBufferPeriod = TimeSpan.Zero;
        execution.ShardLoadLookaheadPeriod = TimeSpan.Zero;
        execution.ShardCheckInterval = settings.ShardCheckInterval;
        execution.ShardStripeCount = settings.ShardStripeCount;
        execution.JobStatusPollInterval = settings.JobStatusPollInterval;
        execution.MaxConcurrentJobsPerSilo = settings.MaximumConcurrentJobs;
        execution.OverloadBackoffDelay = settings.OverloadBackoffDelay;
        execution.ConcurrencySlowStartEnabled = false;
        execution.ShardBatchLingerDelay = TimeSpan.Zero;
        execution.MaxAdoptedCount = settings.MaximumAdoptedCount;
        execution.ShardClaimInitialBudget = settings.InitialClaimBudget;
        execution.ShardClaimMaxBudget = settings.MaximumClaimBudget;
        execution.ShardClaimRampUpDuration = settings.ClaimRampUpDuration;
        execution.ShouldRetry = (context, error) => Retry(context, error, settings, clock);
    }

    private static DateTimeOffset? Retry(IJobRunContext context, Exception error, RuntimeDurableJobOptions settings, TimeProvider clock)
    {
        if (context.DequeueCount >= settings.MaximumAttempts
            || error is KeyLoadException
            {
                Code: ErrorCode.PermissionDenied or ErrorCode.Unauthenticated
                or ErrorCode.Validation or ErrorCode.Corruption or ErrorCode.FormatUnsupported
            })
        {
            return null;
        }
        return clock.GetUtcNow() + settings.RetryDelay;
    }
}
#pragma warning restore ORLEANSEXP005
