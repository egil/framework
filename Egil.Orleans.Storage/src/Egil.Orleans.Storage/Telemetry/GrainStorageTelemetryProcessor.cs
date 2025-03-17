using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace Egil.Orleans.Storage.Telemetry;

internal sealed class GrainStorageTelemetryProcessor : BaseProcessor<Activity>
{
    private readonly Sampler sampler;
    private readonly Func<Activity, bool> activityMatcher;

    public GrainStorageTelemetryProcessor(
            double samplingProbability,
            string? storageName = null,
            string? stateName = null)
    {
        sampler = new TraceIdRatioBasedSampler(samplingProbability);

        if (stateName is not null && storageName is not null)
        {
            var operationNamePrefix = $"{storageName}.{stateName}.";
            activityMatcher = activity => activity.OperationName.StartsWith(operationNamePrefix, StringComparison.Ordinal);
        }
        else if (stateName is not null && storageName is null)
        {
            var operationNameContains = $".{stateName}.";
            activityMatcher = activity => activity.OperationName.Contains(operationNameContains, StringComparison.Ordinal);
        }
        else if (stateName is null && storageName is not null)
        {
            var operationNamePrefix = $"{storageName}.";
            activityMatcher = activity => activity.OperationName.StartsWith(operationNamePrefix, StringComparison.Ordinal);
        }
        else
        {
            activityMatcher = static _ => true;
        }
    }

    public override void OnStart(Activity activity)
    {
        if (activity.Kind is not ActivityKind.Client || activity.Source != GrainStorageTelemetryEnricher.ActivitySource)
        {
            return;
        }

        if (activityMatcher.Invoke(activity))
        {
            var samplingParameters = new SamplingParameters(
                new ActivityContext(
                    activity.TraceId,
                    activity.SpanId,
                    activity.ActivityTraceFlags
                ),
                activity.TraceId,
                activity.DisplayName,
                activity.Kind,
                activity.TagObjects,
                activity.Links
            );

            var result = sampler.ShouldSample(in samplingParameters);
            activity.IsAllDataRequested = result.Decision is SamplingDecision.RecordAndSample;
            activity.ActivityTraceFlags = result.Decision is SamplingDecision.RecordAndSample
                ? ActivityTraceFlags.Recorded
                : ActivityTraceFlags.None;
        }
    }
}
