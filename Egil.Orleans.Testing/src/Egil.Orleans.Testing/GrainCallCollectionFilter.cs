using System.Runtime.ExceptionServices;
using Orleans;
using Orleans.Runtime;

namespace Egil.Orleans.Testing;

internal sealed class GrainCallCollectionFilter(GrainActivityCollector collector) : IIncomingGrainCallFilter
{
    public async Task Invoke(IIncomingGrainCallContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var interfaceTypeText = context.InterfaceType.ToString() ?? string.Empty;
        var isSystemCall = interfaceTypeText.StartsWith("Orleans.Runtime.", StringComparison.Ordinal);
        var isAssertionScope = RequestContext.Get(RequestContextScope.AssertionKey) is true;

        Exception? failure = null;
        try
        {
            await context.Invoke().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        if (!isSystemCall && !isAssertionScope)
        {
            collector.OnGrainCall(context);
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
