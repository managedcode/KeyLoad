using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class NativeCanonicalModelCutAdmissionFixture : IAsyncDisposable
{
    private const int SingleGate = 1;
    private const string SourcePartitionKey = "source";
    private readonly CancellationTokenSource stopping = null!;
    private readonly IHost host = null!;
    internal RequestCqrsProbeLifecycle Lifecycle { get; private set; } = null!;
    internal NativeCanonicalModelCutAdmission Admission { get; private set; } = null!;
    internal IHostApplicationLifetime ApplicationLifetime { get; private set; } = null!;

    internal NativeCanonicalModelCutAdmissionFixture()
    {
        try
        {
            stopping = new();
            host = new HostBuilder().Build();
            ApplicationLifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
            var settings = new RequestProbeExecutionOptions { MaximumActiveGates = SingleGate };
            settings.Validate();
            var execution = Options.Create(settings);
            Lifecycle = new(execution);
            // Open/Token/Dispose only: this fixture never borrows or validates probe files.
            Admission = new(null!, Lifecycle, ApplicationLifetime, execution, TimeProvider.System, stopping.Token);
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            if (host is not null)
            {
                ServerFailureObserver.Observe(host.Dispose, failures);
            }
            if (stopping is not null)
            {
                ServerFailureObserver.Observe(stopping.Dispose, failures);
            }
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    internal void CancelStopping() => stopping.Cancel();

    internal static async Task RunOwnedAsync(Func<NativeCanonicalModelCutAdmissionFixture, Task> action)
    {
        var failures = new List<Exception>();
        NativeCanonicalModelCutAdmissionFixture? fixture = null;
        await ServerFailureObserver.ObserveAsync(() =>
        {
            fixture = new();
            return action(fixture);
        }, failures);
        if (fixture is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.DisposeAsync().AsTask(), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static NativeCanonicalModelCutRequest Request()
    {
        var source = new PartitionRef("cut-tenant", "cut-database", "cut-domain", SourcePartitionKey);
        var destination = source with
        {
            PartitionKey = "destination"
        };
        return new(Guid.NewGuid(), RequestCqrsProbeCodecInput.ArmId, RequestCqrsProbeCodecInput.SessionId,
            source, destination, RequestCqrsProbeCodecInput.CommandId, RequestCqrsProbeCodecInput.TestPrincipal,
            "cut-technical", new(source, "source-queue"), Guid.NewGuid(), new(destination, "target-queue"));
    }

    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(stopping.Cancel, failures);
        ServerFailureObserver.Observe(ApplicationLifetime.StopApplication, failures);
        await ServerFailureObserver.ObserveAsync(Lifecycle.StopAdmissionAndJoin, failures);
        try
        {
            host.Dispose();
        }
        catch (Exception error) when (global::ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is null)
        {
            failures.Add(error);
        }
        catch (Exception error) when (global::ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is not null)
        {
            failures.Add(error);
        }
        try
        {
            stopping.Dispose();
        }
        catch (Exception error) when (global::ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is null)
        {
            failures.Add(error);
        }
        catch (Exception error) when (global::ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is not null)
        {
            failures.Add(error);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
