using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class RemoteTransferRf3Enrollment
{
    internal static Task<TwoRf3MembershipWave> StartAsync(string subject, LocalRf3ImageSelection.Selection? selection, RemoteTransferStartupCallbacks callbacks, CancellationToken token)
    {
        global::KeyLoad.Core.JsonData.Identifier(subject);
        return TwoRf3MembershipWave.StartOwnedAsync(new TwoRf3MembershipWave(selection, register: true, remote: true,
            query: true, probe: false, protectedDocument: true)
        { remoteTransferPrincipalId = subject, startupCallbacks = callbacks }, token);
    }

    internal static void Configure(IDistributedApplicationTestingBuilder builder, string? subject,
        CancellationToken cancellationToken)
    {
        if (subject is null)
        { return; }
        cancellationToken.ThrowIfCancellationRequested();
        global::KeyLoad.Core.JsonData.Identifier(subject);
        foreach (var name in RemoteTransferRf3Protocol.TargetNodes)
        {
            var resource = builder.Resources.OfType<ContainerResource>().Single(value => value.Name == name);
            builder.CreateResourceBuilder(resource).WithEnvironment(RemoteTransferRf3Protocol.SubjectSetting, subject);
        }
    }
}
