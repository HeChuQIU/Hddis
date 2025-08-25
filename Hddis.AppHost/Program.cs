var builder = DistributedApplication.CreateBuilder(args);

var apiService = builder.AddProject<Projects.Hddis_ApiService>("apiservice");
var dataNode = builder.AddProject<Projects.Hddis_DataNode>("datanode");

var hddisService =
    builder.AddContainer("hddis-resp", "hechuqiu/hddis-resp")
        .WithReference(dataNode)
        .WithEndpoint(6379, 6379, "tcp", "redis");

builder.AddProject<Projects.Hddis_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithReference(apiService)
    .WaitFor(apiService);

builder.Build().Run();