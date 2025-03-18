# Extensions to Orleans Storage

This library provides OpenTelemetry integration for Microsoft Orleans grain storage providers. It enables detailed telemetry collection for grain storage operations with minimal configuration, helping you monitor and analyze storage performance, errors, and usage patterns in your Orleans applications.

## Installation

Install the package from NuGet: https://www.nuget.org/packages/Egil.Orleans.Storage

## Usage

### Adding Grain Storage Telemetry

Add telemetry enrichment for all registered grain storage providers:

```csharp
siloBuilder.AddGrainStorageTelemetry();
```

This extension method wraps each registered grain storage provider with a telemetry enricher that creates spans for storage operations and collects metrics for read, write and clear operations.

### Handling GET Operations for Missing Entities

Some storage providers (notably Azure Storage) mark read operations for non-existent entities as errors, which isn't always appropriate in Orleans. This processor ensures such operations are marked as successful in telemetry:

```csharp
siloBuilder.Services.AddGrainStorageGetAlwaysOkTelemetryProcessor();
```

### Configuring Sampling for Storage Telemetry

To prevent excessive telemetry collection in high-volume applications, configure sampling for storage operations:

```csharp
siloBuilder.AddGrainStorageTelemetrySamplingProcessor(
    samplingProbability: 0.1,
    storageName: "MyStorage");
```

Parameters:

- `samplingProbability`: Value between 0.0 and 1.0 determining what percentage of operations to trace (0.1 = 10%)
- `storageName`: Optional filter to apply sampling only to a specific storage provider
- `stateName`: Optional filter to apply sampling only to a specific grain state

## OpenTelemetry Configuration

To properly collect and export Orleans grain storage telemetry, configure your OpenTelemetry setup as shown (this extends the default configuration in Aspire enabled projects):

```csharp
public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
{
    builder.Logging.AddOpenTelemetry(logging =>
    {
        logging.IncludeFormattedMessage = true;
        logging.IncludeScopes = true;
    });

    builder.Services.AddOpenTelemetry()
        .WithMetrics(metrics =>
        {
            metrics.AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter("Microsoft.Orleans")
                .AddMeter("Microsoft.Orleans.GrainStorage"); // <-- Add this line to enable storage metrics
        })
        .WithTracing(tracing =>
        {
            tracing.AddSource("Microsoft.Orleans.Application");
            tracing.AddSource("Microsoft.Orleans.GrainStorage"); // <-- Add this line to enable storage tracing

            tracing.AddSource(builder.Environment.ApplicationName)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation();
        });

    builder.AddOpenTelemetryExporters();

    return builder;
}

private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
{
    if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
    {
        builder.Services
            .AddOpenTelemetry()
            .UseOtlpExporter()
            // Recommended to allow parent sampler to influence child spans.
            .WithTracing(tracing => tracing.SetSampler(
                new ParentBasedSampler(
                    new AlwaysOnSampler())));
    }

    if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
    {
        builder.Services
            .AddOpenTelemetry()
            .UseAzureMonitor()
            // Recommended to allow parent sampler to influence child spans.
            .WithTracing(tracing => tracing.SetSampler(
                new ParentBasedSampler(
                new AlwaysOnSampler())));
    }

    return builder;
}
```

The key components in this configuration:
- Adding the `Microsoft.Orleans.GrainStorage` meter to collect storage-specific metrics
- Adding the `Microsoft.Orleans.GrainStorage` activity source to capture detailed traces
- Configuring exporters like OTLP (OpenTelemetry Protocol) or Azure Monitor

## Collected Telemetry

### Metrics

- `orleans-storage-read`: Count of read operations with storage name and state name dimensions
- `orleans-storage-write`: Count of write operations with storage name and state name dimensions
- `orleans-storage-clear`: Count of clear operations with storage name and state name dimensions

### Traces

Storage operations generate spans with the following attributes:

- Storage name
- State name
- Operation type (Read/Write/Clear)