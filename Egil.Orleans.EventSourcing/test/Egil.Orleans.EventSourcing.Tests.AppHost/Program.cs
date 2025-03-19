var builder = DistributedApplication.CreateBuilder(args);

var persistentStorage = builder
    .AddAzureStorage("azureStorage")
    .RunAsEmulator(config => config.WithLifetime(ContainerLifetime.Persistent));

var logStorage = persistentStorage.AddBlobs("logStorage");

builder.Build().Run();
