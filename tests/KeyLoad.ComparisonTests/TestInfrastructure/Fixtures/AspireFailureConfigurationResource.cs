using Aspire.Hosting.ApplicationModel;

namespace KeyLoad.ComparisonTests.Features.TestInfrastructure;

internal sealed class AspireFailureConfigurationResource(string name) : Resource(name), IResourceWithoutLifetime;
