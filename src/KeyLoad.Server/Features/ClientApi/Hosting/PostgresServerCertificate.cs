using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal sealed class PostgresServerCertificate : IDisposable
{
    internal PostgresServerCertificate(IOptions<PostgresSessionOptions> options)
    {
        var settings = options.Value;
        settings.Validate();
        Value = X509CertificateLoader.LoadPkcs12FromFile(settings.CertificatePath, null, X509KeyStorageFlags.EphemeralKeySet);
        if (!Value.HasPrivateKey)
        {
            var failures = new List<Exception>
            {
                new OptionsValidationException(PostgresSessionOptions.Section, typeof(PostgresSessionOptions), [CertificateInvalid])
            };
            ServerFailureObserver.Observe(Value.Dispose, failures);
            ServerFailureObserver.ThrowIfAny(failures);
        }
    }

    internal X509Certificate2 Value { get; }
    public void Dispose() => Value.Dispose();
    private const string CertificateInvalid = "Native SQL requires an owned TLS certificate with a private key.";
}
