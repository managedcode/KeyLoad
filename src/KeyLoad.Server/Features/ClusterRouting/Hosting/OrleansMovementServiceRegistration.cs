using System.Collections.Immutable;
using System.Net;
using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Replication;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.DocumentStorage;
using ManagedCode.Communication.Orleans.Converters;
using ManagedCode.Orleans.Identity.Core.Serializations;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Serialization;

namespace KeyLoad.Server;

internal static class OrleansMovementServiceRegistration
{
    internal static void Register(HostApplicationBuilder builder, NodeOptions options,
        IRemoteDocumentReadRouter? remoteDocuments, IRemoteBlobReadRouter? remoteBlobs,
        IRemotePartitionQueryRouter? remoteQueries, IRemoteDistributedSearchRouter? distributedSearch,
        IControlledDocumentCommandRouter? controlledDocuments, INativePartitionMovementCapture? movementCapture,
        IPartitionMovementDispatcher? movementDispatcher)
    {
        if (options.PhysicalGroupTrust.RemoteDocumentReads)
        {
            builder.Services.AddSingleton<IPhysicalRequestPlacement, PhysicalDocumentRequestPlacement>();
            if (remoteDocuments is not null)
            {
                builder.Services.AddSingleton(remoteDocuments);
            }
            if (remoteBlobs is not null)
            {
                builder.Services.AddSingleton(remoteBlobs);
            }
            if (remoteQueries is not null)
            {
                builder.Services.AddSingleton(remoteQueries);
            }
            if (distributedSearch is not null)
            {
                builder.Services.AddSingleton(distributedSearch);
            }
        }
        if (controlledDocuments is not null)
        {
            builder.Services.AddSingleton(controlledDocuments);
        }
        if (movementCapture is not null)
        {
            builder.Services.AddSingleton(movementCapture);
        }
        if (movementCapture is PartitionMovementSourceOwner sourceOwner)
        {
            builder.Services.AddSingleton(sourceOwner.TransferReads);
        }
        if (movementDispatcher is not null)
        {
            builder.Services.AddSingleton(movementDispatcher);
        }
        if (movementDispatcher is PartitionMovementRuntime { NativeReceiver: { } nativeMovement })
        {
            builder.Services.AddSingleton(nativeMovement);
        }
    }
}
