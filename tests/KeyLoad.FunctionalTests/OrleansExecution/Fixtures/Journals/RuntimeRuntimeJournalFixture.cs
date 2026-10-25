#pragma warning disable ORLEANSEXP005
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Journaling;
using IAsyncInitializer = TUnit.Core.Interfaces.IAsyncInitializer;

namespace KeyLoad.FunctionalTests.OrleansExecution;

internal sealed class RuntimeRuntimeJournalFixture : IAsyncInitializer, IAsyncDisposable
{
    private readonly RequestCqrsHostedRuntimeFixture runtime = new(
        configureServices: RuntimeJournalExecutionConfiguration.Register);

    internal IGrainFactory Grains => runtime.Grains;
    internal IOptions<RuntimeJournalOptions> JournalOptions => runtime.Services
        .GetRequiredService<IOptions<RuntimeJournalOptions>>();
    internal IOptions<RuntimeTestOptions> TimingOptions { get; } = Options.Create(new RuntimeTestOptions());
    internal IJournalStorageProvider Provider => runtime.Services.GetRequiredKeyedService<IJournalStorageProvider>(RuntimeJournalRegistration.ProviderName);
    internal IJournalStorageCatalog Catalog => runtime.Services.GetRequiredKeyedService<IJournalStorageCatalog>(RuntimeJournalRegistration.ProviderName);

    public Task InitializeAsync() => runtime.InitializeAsync(CancellationToken.None);

    public ValueTask DisposeAsync() => runtime.DisposeAsync();
}

#pragma warning restore ORLEANSEXP005
