using Microsoft.AspNetCore.Connections;

namespace KeyLoad.Server;

internal sealed class PostgresProtocolSession(IServiceProvider services, global::Microsoft.Extensions.Options.IOptions<PostgresSessionOptions> configured,
    PostgresRetainedAdmission admission, PostgresCancellationRegistry cancellations)
{
    private PostgresSessionOptions Options => configured.Value;
    internal async Task RunAsync(ConnectionContext context, PostgresStartupDecoder.Startup startup)
    {
        var admissionLifetime = PostgresAdmissionLifetime.Require(context);
        var owner = context.Features.Get<ServerConnectionFeature>()
            ?? throw Errors.Fail(ErrorCode.Unauthenticated, ServerConnectionProtocol.Unavailable);
        var writer = new PostgresWireWriter(context.Transport.Output, Options.MaximumFrameBytes);
        PostgresSessionIdentity? identity = null;
        PostgresCancellationRegistry.Registration? backend = null;
        PostgresSessionCatalog? catalog = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            writer.Integer(PostgresWireWriter.Authentication, PostgresMessageProtocol.PasswordChallenge);
            await writer.FlushAsync(admissionLifetime.Token);
            identity = await ReadIdentityAsync(context, startup.Subject, admissionLifetime.Token);
            _ = await identity.VerifyAsync(services, owner, admissionLifetime.Token);
            backend = cancellations.Register();
            catalog = new PostgresSessionCatalog(configured, admission);
            writer.Integer(PostgresWireWriter.Authentication, PostgresMessageProtocol.AuthenticationOk);
            PostgresSessionReplies.Parameter(writer, PostgresMessageProtocol.ServerVersion, PostgresMessageProtocol.ServerVersionValue);
            PostgresSessionReplies.Parameter(writer, PostgresMessageProtocol.Profile, PostgresMessageProtocol.ProfileValue);
            PostgresSessionReplies.Parameter(writer, PostgresMessageProtocol.Encoding, PostgresStartupDecoder.Utf8);
            PostgresSessionReplies.Parameter(writer, PostgresStartupDecoder.Encoding, PostgresStartupDecoder.Utf8);
            PostgresSessionReplies.Parameter(writer, PostgresMessageProtocol.StandardStrings, PostgresMessageProtocol.StandardStringsOn);
            PostgresSessionReplies.Backend(writer, backend);
            writer.Ready();
            await writer.FlushAsync(admissionLifetime.Token);
            await admissionLifetime.CompleteAsync();
            await new PostgresSessionLoop(services, configured, catalog, writer, admission).RunAsync(context,
                owner, identity, startup.Partition, backend.Session);
        }, failures);
        if (backend is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => cancellations.RemoveAsync(backend), failures);
        }
        if (catalog is not null)
        {
            ServerFailureObserver.Observe(catalog.Dispose, failures);
        }
        if (identity is not null)
        {
            ServerFailureObserver.Observe(identity.Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private async Task<PostgresSessionIdentity> ReadIdentityAsync(ConnectionContext context, string subject, CancellationToken token)
    {
        PostgresSessionIdentity? result = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var password = await PostgresFrameReader.ReadAsync(context.Transport.Input, false,
                Options.MaximumStartupBytes, admission, token);
            await ServerFailureObserver.ObserveAsync(() =>
            {
                if (password?.Type != PostgresMessageProtocol.Password)
                {
                    throw Errors.Fail(ErrorCode.Unauthenticated, ServerProtocol.MissingCredential);
                }
                result = PostgresSessionIdentity.Create(subject, password.Payload, admission, Options.MaximumStartupBytes);
                return Task.CompletedTask;
            }, failures);
        }, failures);
        if (failures.Count != PostgresProtocol.AllRows)
        {
            if (result is not null)
            {
                ServerFailureObserver.Observe(result.Dispose, failures);
            }
            ServerFailureObserver.ThrowIfAny(failures);
        }
        return result ?? throw Errors.Fail(ErrorCode.Unauthenticated, ServerProtocol.MissingCredential);
    }
}
