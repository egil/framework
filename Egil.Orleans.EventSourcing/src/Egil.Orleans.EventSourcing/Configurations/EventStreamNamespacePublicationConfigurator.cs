using Orleans;

namespace Egil.Orleans.EventSourcing.Configurations;

internal class EventStreamNamespacePublicationConfigurator<TEventGrain, TEventBase, TProjection> : IEventStreamNamespacePublicationConfigurator<TEventGrain, TEventBase, TProjection>
    where TEventGrain : IGrainBase
    where TEventBase : notnull
    where TProjection : notnull
{
    private StreamKeySelector? streamKeySelector;

    public StreamKeySelector? StreamKeySelector => streamKeySelector;

    public IEventStreamNamespacePublicationConfigurator<TEventGrain, TEventBase, TProjection> Key(string key)
    {
        streamKeySelector = new ConstantStringKeySelector(key);
        return this;
    }

    public IEventStreamNamespacePublicationConfigurator<TEventGrain, TEventBase, TProjection> Key(Guid key)
    {
        streamKeySelector = new ConstantGuidKeySelector(key);
        return this;
    }

    public IEventStreamNamespacePublicationConfigurator<TEventGrain, TEventBase, TProjection> Key(long key)
    {
        streamKeySelector = new ConstantLongKeySelector(key);
        return this;
    }

    public IEventStreamNamespacePublicationConfigurator<TEventGrain, TEventBase, TProjection> Key(ReadOnlySpan<byte> key)
    {
        streamKeySelector = new ConstantByteArrayKeySelector(key.ToArray());
        return this;
    }

    public IEventStreamNamespacePublicationConfigurator<TEventGrain, TEventBase, TProjection> KeySelector(Func<TEventBase, string> streamKeySelector)
    {
        this.streamKeySelector = new DynamicStringKeySelector<TEventBase>(streamKeySelector);
        return this;
    }

    public IEventStreamNamespacePublicationConfigurator<TEventGrain, TEventBase, TProjection> KeySelector(Func<TEventBase, Guid> streamKeySelector)
    {
        this.streamKeySelector = new DynamicGuidKeySelector<TEventBase>(streamKeySelector);
        return this;
    }

    public IEventStreamNamespacePublicationConfigurator<TEventGrain, TEventBase, TProjection> KeySelector(Func<TEventBase, long> streamKeySelector)
    {
        this.streamKeySelector = new DynamicLongKeySelector<TEventBase>(streamKeySelector);
        return this;
    }

    public IEventStreamNamespacePublicationConfigurator<TEventGrain, TEventBase, TProjection> KeySelector(Func<TEventBase, ReadOnlySpan<byte>> streamKeySelector)
    {
        this.streamKeySelector = new DynamicByteArrayKeySelector<TEventBase>(e => streamKeySelector(e).ToArray());
        return this;
    }

    public IEventStreamNamespacePublicationConfigurator<TEventGrain, TEventBase, TProjection> KeySelector<TEvent>(Func<TEvent, string> streamKeySelector) where TEvent : TEventBase
    {
        this.streamKeySelector = new DynamicStringKeySelector<TEvent>(streamKeySelector);
        return this;
    }

    public IEventStreamNamespacePublicationConfigurator<TEventGrain, TEventBase, TProjection> KeySelector<TEvent>(Func<TEvent, Guid> streamKeySelector) where TEvent : TEventBase
    {
        this.streamKeySelector = new DynamicGuidKeySelector<TEvent>(streamKeySelector);
        return this;
    }

    public IEventStreamNamespacePublicationConfigurator<TEventGrain, TEventBase, TProjection> KeySelector<TEvent>(Func<TEvent, long> streamKeySelector) where TEvent : TEventBase
    {
        this.streamKeySelector = new DynamicLongKeySelector<TEvent>(streamKeySelector);
        return this;
    }

    public IEventStreamNamespacePublicationConfigurator<TEventGrain, TEventBase, TProjection> KeySelector<TEvent>(Func<TEvent, ReadOnlySpan<byte>> streamKeySelector) where TEvent : TEventBase
    {
        this.streamKeySelector = new DynamicByteArrayKeySelector<TEvent>(e => streamKeySelector(e).ToArray());
        return this;
    }
}

// Base class for stream key selectors
internal abstract class StreamKeySelector
{
    public abstract object GetKey(object eventObj);
}

// Constant key selectors
internal sealed class ConstantStringKeySelector(string key) : StreamKeySelector
{
    public override object GetKey(object eventObj) => key;
}

internal sealed class ConstantGuidKeySelector(Guid key) : StreamKeySelector
{
    public override object GetKey(object eventObj) => key;
}

internal sealed class ConstantLongKeySelector(long key) : StreamKeySelector
{
    public override object GetKey(object eventObj) => key;
}

internal sealed class ConstantByteArrayKeySelector(byte[] key) : StreamKeySelector
{
    public override object GetKey(object eventObj) => key;
}

// Dynamic key selectors
internal sealed class DynamicStringKeySelector<TEvent>(Func<TEvent, string> selector) : StreamKeySelector
{
    public override object GetKey(object eventObj) => eventObj is TEvent evt ? selector(evt) : throw new InvalidOperationException($"Expected event of type {typeof(TEvent).Name}, but got {eventObj.GetType().Name}");
}

internal sealed class DynamicGuidKeySelector<TEvent>(Func<TEvent, Guid> selector) : StreamKeySelector
{
    public override object GetKey(object eventObj) => eventObj is TEvent evt ? selector(evt) : throw new InvalidOperationException($"Expected event of type {typeof(TEvent).Name}, but got {eventObj.GetType().Name}");
}

internal sealed class DynamicLongKeySelector<TEvent>(Func<TEvent, long> selector) : StreamKeySelector
{
    public override object GetKey(object eventObj) => eventObj is TEvent evt ? selector(evt) : throw new InvalidOperationException($"Expected event of type {typeof(TEvent).Name}, but got {eventObj.GetType().Name}");
}

internal sealed class DynamicByteArrayKeySelector<TEvent>(Func<TEvent, byte[]> selector) : StreamKeySelector
{
    public override object GetKey(object eventObj) => eventObj is TEvent evt ? selector(evt) : throw new InvalidOperationException($"Expected event of type {typeof(TEvent).Name}, but got {eventObj.GetType().Name}");
}