using Projects;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<ProxyMov_DownloadServer>("proxymov-downloadserver");

builder.Build().Run();