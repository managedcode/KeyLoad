using KeyLoad.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.FunctionalTests.BlobStorage;

internal sealed class FileStorageRouteTests
{
    private const int ExpectedActions = 12;
    private const string UploadRoute = "api/storage/{tenant}/{database}/{domain}/{partition}/{resource}/upload";
    private const string AbortRoute = "api/storage/{tenant}/{database}/{domain}/{partition}/{resource}/upload-chunks/{uploadId}";

    [Test]
    public async Task NativeMvcDiscoveryExposesOnlyAuthorizedFileActions()
    {
        var builder = WebApplication.CreateBuilder();
        FileStorageComposition.Register(builder);
        await using var app = builder.Build();
        var actions = app.Services.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items
            .OfType<ControllerActionDescriptor>().Where(action => action.ControllerTypeInfo.AsType() == typeof(FileStorageController)).ToArray();
        await Assert.That(actions.Length).IsEqualTo(ExpectedActions);
        await Assert.That(actions.Any(action => action.AttributeRouteInfo?.Template == UploadRoute)).IsTrue();
        await Assert.That(actions.Count(action => action.AttributeRouteInfo?.Template == AbortRoute)).IsEqualTo(1);
        foreach (var action in actions)
        {
            await Assert.That(action.EndpointMetadata.OfType<FileStorageCapabilityAttribute>().Count()).IsEqualTo(1);
        }
    }
}
