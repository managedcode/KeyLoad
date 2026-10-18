using KeyLoad;
using KeyLoad.AppHost.Hosting;

const int Completed = 0;
_ = SerializationExecutionRegistration.Process.Value;
var builder = DistributedApplication.CreateBuilder(args);
var runtime = AppHostOptionsRegistration.Get(builder);
KeyLoadResources.AddKeyLoad(builder);
var application = builder.Build();
if (runtime.Control.Value.LoggerModelControl)
{
    return Completed;
}
await using (application)
{
    await application.RunAsync();
}
return Completed;
