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
internal static class NativeRuntimeJournalRegistration
{
    internal const string ProviderName = "keyload-rf3-runtime-journal-v1";
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
        silo.Configure<JournaledStateManagerOptions>(options => options.JournalFormatKey = BinaryFormat);
        services.AddOptions<DurableJobsOptions>().Configure<IOptions<NativeDurableJobOptions>, TimeProvider>(
            (options, settings, clock) => Configure(options, settings.Value, clock));
        silo.UseJournaledDurableJobs();
        NativeRequestDrainRegistration.Register(services);
        NativeJobGraphRegistration.Extend(services);
    }

    private static RuntimeJournalClient CreateClient(IServiceProvider provider)
    {
        var codec = new Lazy<GrainRequestCodec>(provider.GetRequiredService<GrainRequestCodec>);
        var database = new Lazy<DatabaseEngine>(provider.GetRequiredService<DatabaseEngine>);
        var coordinator = new Lazy<Func<ICommitCoordinator>>(provider.GetRequiredService<Func<ICommitCoordinator>>);
        return new RuntimeJournalClient(provider.GetRequiredService<IGrainFactory>(),
            () => codec.Value, () => database.Value, () => coordinator.Value(), provider,
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>>(),
            provider.GetRequiredService<IOptions<GrainRoutingOptions>>(),
            provider.GetRequiredService<IOptions<RuntimeJournalOptions>>(),
            provider.GetRequiredService<RuntimeJournalAdmission>());
    }

    private static void Configure(DurableJobsOptions native, NativeDurableJobOptions settings, TimeProvider clock)
    {
        if (!settings.IsValid())
        {
            throw new InvalidOperationException(NativeDurableJobOptions.ValidationMessage);
        }
        native.ActiveProviderName = ProviderName;
        native.ShardDuration = settings.ShardDuration;
        native.ShardActivationBufferPeriod = TimeSpan.Zero;
        native.ShardLoadLookaheadPeriod = TimeSpan.Zero;
        native.ShardCheckInterval = settings.ShardCheckInterval;
        native.ShardStripeCount = settings.ShardStripeCount;
        native.JobStatusPollInterval = settings.JobStatusPollInterval;
        native.MaxConcurrentJobsPerSilo = settings.MaximumConcurrentJobs;
        native.OverloadBackoffDelay = settings.OverloadBackoffDelay;
        native.ConcurrencySlowStartEnabled = false;
        native.ShardBatchLingerDelay = TimeSpan.Zero;
        native.MaxAdoptedCount = settings.MaximumAdoptedCount;
        native.ShardClaimInitialBudget = settings.InitialClaimBudget;
        native.ShardClaimMaxBudget = settings.MaximumClaimBudget;
        native.ShardClaimRampUpDuration = settings.ClaimRampUpDuration;
        native.ShouldRetry = (context, error) => Retry(context, error, settings, clock);
    }

    private static DateTimeOffset? Retry(IJobRunContext context, Exception error, NativeDurableJobOptions settings, TimeProvider clock)
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
