#pragma warning disable ORLEANSEXP005
using KeyLoad.Core;
using KeyLoad.Orleans;
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

    internal static void Register(ISiloBuilder silo, IOptions<RuntimeJournalOptions> journal,
        IOptions<NativeDurableJobOptions> jobs)
    {
        if (!journal.Value.IsValid() || !jobs.Value.IsValid())
        {
            throw new InvalidOperationException(NativeDurableJobOptions.ValidationMessage);
        }
        var services = silo.Services;
        services.AddSingleton<RuntimeJournalAdmission>();
        services.AddSingleton(provider => new RuntimeJournalClient(provider.GetRequiredService<IGrainFactory>(),
            provider.GetRequiredService<GrainRequestCodec>(), provider.GetRequiredService<DatabaseEngine>(),
            provider.GetRequiredService<ICommitCoordinator>(), provider, provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>>(),
            provider.GetRequiredService<IOptions<GrainRoutingOptions>>(), journal,
            provider.GetRequiredService<RuntimeJournalAdmission>()));
        silo.AddJournalStorage(ProviderName, provider => new RuntimeJournalStorageProvider(
            provider.GetRequiredService<RuntimeJournalClient>(), journal,
            provider.GetRequiredService<IOptions<GrainRoutingOptions>>(),
            provider.GetRequiredService<IOptions<JournaledStateManagerOptions>>()));
        silo.Configure<JournaledStateManagerOptions>(options => options.JournalFormatKey = BinaryFormat);
        var existing = services.Where(NativeJobLifecycleRegistration.IsLifecycle).ToArray();
        services.AddOptions<DurableJobsOptions>().Configure<TimeProvider>((options, clock) => Configure(options, jobs.Value, clock));
        silo.UseJournaledDurableJobs();
        NativeJobLifecycleRegistration.Decorate(services, existing);
        NativeJobGraphRegistration.Extend(services);
    }

    private static void Configure(DurableJobsOptions native, NativeDurableJobOptions settings, TimeProvider clock)
    {
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
