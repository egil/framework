var builder = DistributedApplication.CreateBuilder(args);

var persistentStorage = builder
    .AddAzureStorage("azureStorage")
    .RunAsEmulator(config => config.WithLifetime(ContainerLifetime.Persistent));

persistentStorage.AddBlobs("blobStorage");
persistentStorage.AddTables("tableStorage");

await builder.Build().RunAsync();
