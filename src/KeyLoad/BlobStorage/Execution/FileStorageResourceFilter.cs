using KeyLoad.Core.Features.BlobStorage;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal sealed class FileStorageResourceFilter(IOptions<FileStorageOptions> options, IOptions<NodeOptions> node,
    FileStorageTransferGate transfers)
    : IAsyncResourceFilter
{
    private const string TenantRoute = "tenant";
    private const string DatabaseRoute = "database";
    private const string DomainRoute = "domain";
    private const string PartitionRoute = "partition";
    private const string ResourceRoute = "resource";

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var http = context.HttpContext;
        var routes = http.Request.RouteValues;
        var address = new FileAddress(new(Value(routes, TenantRoute), Value(routes, DatabaseRoute),
            Value(routes, DomainRoute), Value(routes, PartitionRoute)), Value(routes, ResourceRoute), string.Empty);
        var capability = context.ActionDescriptor.EndpointMetadata.OfType<FileStorageCapabilityAttribute>().Single().Capability;
        var state = await CanonicalOperationGateway.ExecuteFileMetadataAsync(http,
            new(address, capability, Guid.Empty), null, http.RequestAborted) as FileMetadataState
            ?? throw Errors.Fail(ErrorCode.Corruption, FileStorageProtocol.Invalid);
        var physicalNamespace = FilePhysicalIdentity.Resource(address, state.Incarnation);
        http.Features.Set(new FileStorageScope(address, state.Incarnation, physicalNamespace,
            Path.Combine(options.Value.ResolveRoot(node.Value), physicalNamespace)));
        using var admitted = await transfers.EnterAsync(http.RequestAborted);
        using var deadline = new CancellationTokenSource(options.Value.TransferTimeout);
        var original = http.RequestAborted;
        using var joined = CancellationTokenSource.CreateLinkedTokenSource(original, deadline.Token);
        http.RequestAborted = joined.Token;
        try
        {
            await next();
        }
        finally
        {
            http.RequestAborted = original;
        }
    }

    private static string Value(RouteValueDictionary routes, string key)
        => routes[key] as string ?? throw Errors.Fail(ErrorCode.Validation, FileStorageProtocol.Invalid);
}

[AttributeUsage(AttributeTargets.Method)]
internal sealed class FileStorageCapabilityAttribute(Capability capability) : Attribute
{
    public Capability Capability { get; } = capability;
}
