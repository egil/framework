namespace Egil.Orleans.EventSourcing;

public interface IEventProjection<TSelf> where TSelf : notnull, IEventProjection<TSelf>
{
    /// <summary>
    /// Creates a new instance of the projection with default values.
    /// </summary>
    /// <returns>A new instance of the projection.</returns>
    static abstract TSelf CreateDefault();
}
