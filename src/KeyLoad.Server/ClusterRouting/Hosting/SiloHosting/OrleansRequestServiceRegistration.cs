using System.Security.Cryptography;
using KeyLoad.Clustering;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal static class OrleansRequestServiceRegistration
{
    internal static void Register(IServiceCollection services)
    {
        services.AddSingleton(provider =>
        {
            var runtime = provider.GetRequiredService<ServerRuntimeOptions>();
            var node = runtime.Node.Value;
            var signingKey = Convert.FromBase64String(node.SigningKey);
            try
            {
                return new GrainRequestCodec(node.Incarnation, signingKey, runtime.Core.DatabaseLimits,
                    provider.GetRequiredService<TimeProvider>(), provider.GetRequiredService<IOptions<GrainRoutingOptions>>())
                {
                    PhaseObserver = provider.GetService<IGrainRequestPhaseObserver>(),
                    PurposeObserver = provider.GetService<IGrainRequestPurposeObserver>()
                };
            }
            finally
            {
                CryptographicOperations.ZeroMemory(signingKey);
            }
        });
    }
}
