using KeyLoad;
using KeyLoad.AppHost.Hosting;
using KeyLoad.AppHost.Features.TestInfrastructure;

const string MessageText = "An explicitly selected test suite must not be empty.";
const int EmptyResult = 0;

_ = SerializationExecutionRegistration.Process.Value;
var requested = TestSuiteSettings.Requested(args);
var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions
{
    Args = args,
    DisableDashboard = requested
});
var runtimeOptions = AppHostOptionsRegistration.Get(builder);
var tests = runtimeOptions.Control.Value.Tests;
var loggerModelControl = runtimeOptions.Control.Value.LoggerModelControl;
if (requested && tests is null)
{
    throw new InvalidOperationException(MessageText);
}
KeyLoadResources.AddKeyLoad(builder);
var app = builder.Build();
if (loggerModelControl)
{
    return EmptyResult;
}
if (tests is not null)
{
    return await TestSuiteApplication.RunAsync(app, tests).ConfigureAwait(false);
}
await using (app.ConfigureAwait(false))
{
    await app.RunAsync().ConfigureAwait(false);
}
return EmptyResult;
