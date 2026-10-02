using KeyLoad.AppHost.Hosting;

var builder = DistributedApplication.CreateBuilder(args);
KeyLoadAppHostApplication.AddKeyLoad(builder);
await builder.Build().RunAsync();
