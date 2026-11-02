using KeyLoad.Core.BlobStorage;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal sealed class FileStorageResourceFilter(IOptions<FileStorageOptions> options, IOptions<NodeOptions> node)
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
        var capability = ((context.ActionDescriptor.EndpointMetadata /* KLD0041: Inspect native MVC endpoint metadata only to select the concrete FileStorageCapabilityAttribute for this file operation. */).OfType<FileStorageCapabilityAttribute>() /* KLD0041: Inspect native MVC endpoint metadata only to select the concrete FileStorageCapabilityAttribute for this file operation. */).Single().Capability;
        var state = await CanonicalOperationGateway.ExecuteFileMetadataAsync<FileMetadataState>(http,
            new(address, capability, Guid.Empty), null, http.RequestAborted);
        var physicalNamespace = FilePhysicalIdentity.Resource(address, state.Incarnation);
        http.Features.Set(new FileStorageScope(address, state.Incarnation, physicalNamespace,
            Path.Combine(options.Value.ResolveRoot(node.Value), physicalNamespace)));
        await next();
    }

    private static string Value(RouteValueDictionary routes, string key)
        => ((routes[key] /* KLD0041: ASP.NET route values use the native object-valued dictionary; this file route accepts only a string. */) as string /* KLD0042: Require the ASP.NET route value to be a string before constructing a canonical file address. */) ?? throw Errors.Fail(ErrorCode.Validation, FileStorageProtocol.Invalid);
}

[AttributeUsage(AttributeTargets.Method)]
internal sealed class FileStorageCapabilityAttribute(Capability capability) : Attribute
{
    public Capability Capability { get; } = capability;
}
