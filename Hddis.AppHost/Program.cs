using CommunityToolkit.Aspire.Hosting.Dapr;

var builder = DistributedApplication.CreateBuilder(args);

var apiService = builder.AddProject<Projects.Hddis_ApiService>("apiservice");
var dataNode = builder.AddProject<Projects.Hddis_DataNode>("datanode");

var hddisService = builder
    .AddContainer("hddis-resp", "hechuqiu/hddis-resp")
    .WithReference(dataNode)
    // .WithEndpoint(16379, 6379, "tcp", "redis")
    .WithDaprSidecar(new DaprSidecarOptions
    {
        AppId = "hddis-redis",
        AppPort = 6379,
        EnableAppHealthCheck = true,
        DaprHttpPort = 3500,
        DaprGrpcPort = 50001,
        Config = "pipeline"
    });

builder
    .AddProject<Projects.Hddis_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithReference(apiService)
    .WaitFor(apiService);

builder.Build().Run();
