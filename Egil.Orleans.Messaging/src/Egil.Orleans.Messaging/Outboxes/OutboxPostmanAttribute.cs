namespace Egil.Orleans.Messaging.Outboxes;

/// <summary>
/// Names a postman implementation for keyed outbox postman registration.
/// </summary>
/// <param name="name">The keyed postman name.</param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class OutboxPostmanAttribute(string name) : Attribute
{
    /// <summary>
    /// The keyed postman name.
    /// </summary>
    public string Name { get; } = name;
}