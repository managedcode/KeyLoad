using System.Reflection;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace KeyLoad.Server;

internal sealed class FileStorageControllerDiscovery : IApplicationFeatureProvider<ControllerFeature>
{
    public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        => feature.Controllers.Add(typeof(FileStorageController).GetTypeInfo());
}
