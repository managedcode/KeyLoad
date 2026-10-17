using System.Text.Json;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.CrashHost.Features.EventStreams;
using static KeyLoad.CrashHost.Features.EventStreams.SharedDomainProtocol;

namespace KeyLoad.RecoveryTests.Features.EventStreams;

/// <summary>Derives literal expected lifetimes only from the original child-issued resource commands.</summary>
internal static class SharedDomainRecoveryReferences
{
    internal static async Task<Dictionary<string, ResourceLifetimeV1>> CaptureAsync(SharedDomainRecoveryInput input)
    {
        var definitions = SharedDomainCorpus.Create(Tenant, Database).SelectMany(scope => scope.Resources).ToArray();
        await Assert.That(input.ResourceCreations.Length).IsEqualTo(definitions.Length);
        var lifetimes = new Dictionary<string, ResourceLifetimeV1>(StringComparer.Ordinal);
        var commandIds = new HashSet<Guid>();
        foreach (var original in input.ResourceCreations)
        {
            await Assert.That(original.Id).IsNotEqualTo(Guid.Empty);
            await Assert.That(commandIds.Add(original.Id)).IsTrue();
            await Assert.That(original.Kind).IsEqualTo(OperationKind.ConfigureResource);
            await Assert.That(original.PrincipalId).IsEqualTo(Root);
            var request = JsonSerializer.Deserialize<ConfigureResourceRequest>(original.PayloadJson, JsonDefaults.Options)
                ?? throw new InvalidOperationException(MissingNative);
            var definition = definitions.Single(value => value.Name == request.Definition.Name);
            await SharedDomainRecoveryValues.EqualAsync(request, new ConfigureResourceRequest(Tenant, Database, definition));
            lifetimes.Add(definition.Name, ResourceLifetimeIdentity.Create(original, Tenant, Database, definition));
        }
        await Assert.That(lifetimes.Count).IsEqualTo(definitions.Length);
        return lifetimes;
    }
}
