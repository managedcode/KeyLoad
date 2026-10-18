using KeyLoad.Core.Features.ResourceExecution;
using static KeyLoad.RecoveryTests.Features.EventStreams.SharedDomainProtocol;

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
            await Assert.That(original.CommandId).IsNotEqualTo(Guid.Empty);
            await Assert.That(commandIds.Add(original.CommandId)).IsTrue();
            await Assert.That(original.Value!.Kind).IsEqualTo(OperationKind.ConfigureResource);
            await Assert.That(original.UserId).IsEqualTo(Root);
            var request = NativeSerialization.Deserialize<ConfigureResourceRequest>(original.Value.Input.Span);
            var definition = definitions.Single(value => value.Name == request.Definition.Name);
            await SharedDomainRecoveryValues.EqualAsync(request, new ConfigureResourceRequest(Tenant, Database, definition));
            lifetimes.Add(definition.Name, ResourceLifetimeIdentity.Create(original, Tenant, Database, definition));
        }
        await Assert.That(lifetimes.Count).IsEqualTo(definitions.Length);
        return lifetimes;
    }
}
