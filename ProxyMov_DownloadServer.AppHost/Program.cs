var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.ProxyMov_DownloadServer>("proxymov-downloadserver");

builder.Build().Run();
