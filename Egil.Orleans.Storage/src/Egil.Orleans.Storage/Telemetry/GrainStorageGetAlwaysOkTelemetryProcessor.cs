using System.Diagnostics;
using OpenTelemetry;

namespace Egil.Orleans.Storage.Telemetry;

/// <summary>
/// Some storage providers, notable Azure Storage, will mark an read from a non-existing entity as an error,
/// which is not the case for Orleans. This processor will mark such activities as successful.
/// </summary>
internal sealed class GrainStorageGetAlwaysOkTelemetryProcessor : BaseProcessor<Activity>
{
    public override void OnEnd(Activity data)
    {
        if (data.Parent?.Source == GrainStorageTelemetryEnricher.ActivitySource &&
            data.Kind is ActivityKind.Client &&
            data.DisplayName == "GET" &&
            data.Status is ActivityStatusCode.Error)
        {
            data.SetStatus(ActivityStatusCode.Ok);
        }
    }
}
