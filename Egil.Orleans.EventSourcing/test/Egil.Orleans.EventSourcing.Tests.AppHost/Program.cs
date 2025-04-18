var builder = DistributedApplication.CreateBuilder(args);

var persistentStorage = builder
    .AddAzureStorage("azureStorage")
    .RunAsEmulator(config => config.WithLifetime(ContainerLifetime.Persistent));

persistentStorage.AddBlobs("logStorage");

await builder.Build().RunAsync();
