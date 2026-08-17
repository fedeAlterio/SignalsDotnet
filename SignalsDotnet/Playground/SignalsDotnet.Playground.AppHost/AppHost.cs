var builder = DistributedApplication.CreateBuilder(args);

var playground = builder.AddProject<Projects.SignalsDotnet_Playground>("playground");

builder.AddProject<Projects.SignalsDotnet_Playground_Client>("client")
       .WithReference(playground)
       .WaitFor(playground);

builder.Build().Run();
