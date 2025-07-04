using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Hosting;
using Orleans.Runtime;
using Egil.Orleans.EventSourcing.Examples;

namespace Egil.Orleans.EventSourcing.Examples;

/// <summary>
/// Complete example showing how to use Orleans-style event sourcing with attribute-based dependency injection.
/// This example demonstrates the recommended patterns for production applications.
/// </summary>
public class CompleteOrleansStyleExample
{
    /// <summary>
    /// Example of configuring Orleans silo with multiple event storage configurations.
    /// </summary>
    public static async Task Main(string[] args)
    {
        var host = Host.CreateDefaultBuilder(args)
            .UseOrleans(siloBuilder =>
            {
                siloBuilder
                    .UseLocalhostClustering()
                    .ConfigureLogging(logging => logging.AddConsole())
                    
                    // 1. Add Orleans-style event sourcing support
                    .AddOrleansEventSourcing()
                    
                    // 2. Configure specific event storage for user domain
                    .AddAzureTableEventStorage<UserEvent, UserOutboxEvent>(
                        "user-events",
                        "UseDevelopmentStorage=true") // Use Azurite for local development
                    
                    // 3. Configure shared scope storage for order domain
                    .AddEventStorage<OrderEvent, OrderOutboxEvent>(
                        "order-events-shared", 
                        builder => builder
                            .UseEventSerializer(CreateCustomSerializer<OrderEvent>())
                            .UseOutboxEventSerializer(CreateCustomSerializer<OrderOutboxEvent>())
                            .UseStorageProvider(CreateCustomStorageProvider())
                            .UseRetentionPolicy(new TimeBasedRetentionPolicy(TimeSpan.FromDays(365)))
                            .UseDeduplicationStrategy(DeduplicationStrategy.ContentHash))
                    
                    // 4. Configure in-memory storage for testing
                    .AddInMemoryEventStorage<TestEvent, TestOutboxEvent>(
                        "test-events");
            })
            .Build();

        await host.RunAsync();
    }

    private static Egil.Orleans.EventSourcing.Serialization.IEventSerializer<T> CreateCustomSerializer<T>() where T : class
    {
        // Create custom serializer implementation
        throw new NotImplementedException("Implement custom serializer");
    }

    private static IEventStorageProvider CreateCustomStorageProvider()
    {
        // Create custom storage provider implementation
        throw new NotImplementedException("Implement custom storage provider");
    }
}

#region Complete Grain Examples

/// <summary>
/// Example grain using Orleans persistent state for projection storage.
/// This is the recommended pattern for most scenarios.
/// </summary>
public class ProductionUserGrain : EventGrain<UserProjection, UserEvent, UserOutboxEvent>, IGrainWithStringKey
{
    private readonly IEmailService emailService;

    public ProductionUserGrain(
        [PersistentState("projection", "user-projections")] 
        IPersistentState<ProjectionState<UserProjection>> projectionState,
        [EventStorage("user-events")] 
        IEventStorage<UserEvent, UserOutboxEvent> eventStorage,
        IEmailService emailService,
        ILogger<ProductionUserGrain> logger)
        : base(projectionState, eventStorage, logger: logger)
    {
        this.emailService = emailService;
    }

    public override async Task ProcessEventAsync(object @event, CancellationToken cancellationToken = default)
    {
        switch (@event)
        {
            case UserCreated userCreated:
                await ProcessUserCreatedAsync(userCreated, cancellationToken);
                break;
            case UserEmailChanged emailChanged:
                await ProcessUserEmailChangedAsync(emailChanged, cancellationToken);
                break;
            case UserDeactivated userDeactivated:
                await ProcessUserDeactivatedAsync(userDeactivated, cancellationToken);
                break;
            default:
                throw new ArgumentException($"Unknown event type: {@event.GetType()}");
        }
    }

    protected override void ConfigureEventStreams(EventStreamBuilder<UserProjection> builder)
    {
        builder
            .ForStream("user-lifecycle")
            .OnEvent<UserCreated>()
                .HandleAsync(async (evt, projection, outbox) =>
                {
                    logger.LogInformation("User {UserId} created with email {Email}", evt.UserId, evt.Email);

                    // Apply event to projection
                    var updatedProjection = projection with
                    {
                        UserId = evt.UserId,
                        Name = evt.Name,
                        Email = evt.Email,
                        IsActive = true,
                        CreatedAt = evt.Timestamp,
                        LastModifiedAt = evt.Timestamp
                    };

                    // Add outbox events for side effects
                    outbox.Add(new UserNotificationRequested(
                        Guid.NewGuid().ToString(),
                        evt.UserId,
                        $"Welcome {evt.Name}! Your account has been created.",
                        evt.Timestamp));

                    outbox.Add(new UserAnalyticsEvent(
                        Guid.NewGuid().ToString(),
                        evt.UserId,
                        "UserRegistered",
                        new Dictionary<string, object>
                        {
                            ["email"] = evt.Email,
                            ["name"] = evt.Name
                        },
                        evt.Timestamp));

                    return updatedProjection;
                })
            .OnEvent<UserEmailChanged>()
                .HandleAsync(async (evt, projection, outbox) =>
                {
                    logger.LogInformation("User {UserId} email changed from {OldEmail} to {NewEmail}", 
                        evt.UserId, evt.OldEmail, evt.NewEmail);

                    // Validate email change (async operation)
                    await ValidateEmailChangeAsync(evt.OldEmail, evt.NewEmail);

                    var updatedProjection = projection with
                    {
                        Email = evt.NewEmail,
                        LastModifiedAt = evt.Timestamp
                    };

                    // Add outbox events
                    outbox.Add(new UserEmailUpdateRequested(
                        Guid.NewGuid().ToString(),
                        evt.UserId,
                        evt.NewEmail,
                        evt.Timestamp));

                    outbox.Add(new UserAnalyticsEvent(
                        Guid.NewGuid().ToString(),
                        evt.UserId,
                        "EmailChanged",
                        new Dictionary<string, object>
                        {
                            ["oldEmail"] = evt.OldEmail,
                            ["newEmail"] = evt.NewEmail
                        },
                        evt.Timestamp));

                    return updatedProjection;
                })
            .OnEvent<UserDeactivated>()
                .HandleAsync(async (evt, projection, outbox) =>
                {
                    logger.LogInformation("User {UserId} deactivated: {Reason}", evt.UserId, evt.Reason);

                    var updatedProjection = projection with
                    {
                        IsActive = false,
                        LastModifiedAt = evt.Timestamp
                    };

                    outbox.Add(new UserNotificationRequested(
                        Guid.NewGuid().ToString(),
                        evt.UserId,
                        $"Your account has been deactivated. Reason: {evt.Reason}",
                        evt.Timestamp));

                    return updatedProjection;
                });
    }

    private async Task ProcessUserCreatedAsync(UserCreated evt, CancellationToken cancellationToken)
    {
        // Apply the event using the configured stream builder
        await ApplyEventAsync(evt, cancellationToken);
    }

    private async Task ProcessUserEmailChangedAsync(UserEmailChanged evt, CancellationToken cancellationToken)
    {
        await ApplyEventAsync(evt, cancellationToken);
    }

    private async Task ProcessUserDeactivatedAsync(UserDeactivated evt, CancellationToken cancellationToken)
    {
        await ApplyEventAsync(evt, cancellationToken);
    }

    private async Task ValidateEmailChangeAsync(string oldEmail, string newEmail)
    {
        // Example async validation
        await Task.Delay(100); // Simulate validation call
        
        if (string.IsNullOrWhiteSpace(newEmail))
        {
            throw new ArgumentException("New email cannot be empty");
        }
    }

    // Public API methods
    public async Task CreateUserAsync(string name, string email)
    {
        var evt = new UserCreated(this.GetPrimaryKeyString(), name, email, DateTimeOffset.UtcNow);
        await ProcessEventAsync(evt);
    }

    public async Task ChangeEmailAsync(string newEmail)
    {
        var evt = new UserEmailChanged(
            this.GetPrimaryKeyString(), 
            Projection.Email, 
            newEmail, 
            DateTimeOffset.UtcNow);
        await ProcessEventAsync(evt);
    }

    public async Task DeactivateUserAsync(string reason)
    {
        var evt = new UserDeactivated(this.GetPrimaryKeyString(), reason, DateTimeOffset.UtcNow);
        await ProcessEventAsync(evt);
    }

    public Task<UserProjection> GetUserAsync() => Task.FromResult(Projection);
}

/// <summary>
/// Example grain using shared transaction scope for both projection and events.
/// This pattern is useful when you need atomic consistency between events and projection.
/// </summary>
public class OrderGrain : EventGrain<OrderProjection, OrderEvent, OrderOutboxEvent>, IGrainWithStringKey
{
    public OrderGrain(
        [EventStorage("order-events-shared")] 
        IEventStorage<OrderEvent, OrderOutboxEvent> eventStorage,
        ILogger<OrderGrain> logger)
        : base(eventStorage, logger: logger) // No Orleans persistent state - using shared scope
    {
    }

    public override async Task ProcessEventAsync(object @event, CancellationToken cancellationToken = default)
    {
        switch (@event)
        {
            case OrderCreated orderCreated:
                await ApplyEventAsync(orderCreated, cancellationToken);
                break;
            case OrderItemAdded itemAdded:
                await ApplyEventAsync(itemAdded, cancellationToken);
                break;
            case OrderShipped orderShipped:
                await ApplyEventAsync(orderShipped, cancellationToken);
                break;
            default:
                throw new ArgumentException($"Unknown event type: {@event.GetType()}");
        }
    }

    protected override void ConfigureEventStreams(EventStreamBuilder<OrderProjection> builder)
    {
        builder
            .ForStream("order-lifecycle")
            .OnEvent<OrderCreated>()
                .HandleAsync(async (evt, projection, outbox) =>
                {
                    var updatedProjection = projection with
                    {
                        OrderId = evt.OrderId,
                        CustomerId = evt.CustomerId,
                        Status = OrderStatus.Created,
                        CreatedAt = evt.Timestamp,
                        LastModifiedAt = evt.Timestamp,
                        Items = new List<OrderItem>(),
                        TotalAmount = 0m
                    };

                    outbox.Add(new OrderNotificationRequested(
                        Guid.NewGuid().ToString(),
                        evt.OrderId,
                        evt.CustomerId,
                        "Order created successfully",
                        evt.Timestamp));

                    return updatedProjection;
                })
            .OnEvent<OrderItemAdded>()
                .HandleAsync(async (evt, projection, outbox) =>
                {
                    var newItem = new OrderItem(evt.ProductId, evt.Quantity, evt.UnitPrice);
                    var updatedItems = projection.Items.Concat(new[] { newItem }).ToList();
                    var newTotal = updatedItems.Sum(item => item.Quantity * item.UnitPrice);

                    var updatedProjection = projection with
                    {
                        Items = updatedItems,
                        TotalAmount = newTotal,
                        LastModifiedAt = evt.Timestamp
                    };

                    outbox.Add(new InventoryReservationRequested(
                        Guid.NewGuid().ToString(),
                        evt.ProductId,
                        evt.Quantity,
                        evt.OrderId,
                        evt.Timestamp));

                    return updatedProjection;
                });
    }

    // Public API methods
    public async Task CreateOrderAsync(string customerId)
    {
        var evt = new OrderCreated(this.GetPrimaryKeyString(), customerId, DateTimeOffset.UtcNow);
        await ProcessEventAsync(evt);
    }

    public async Task AddItemAsync(string productId, int quantity, decimal unitPrice)
    {
        var evt = new OrderItemAdded(this.GetPrimaryKeyString(), productId, quantity, unitPrice, DateTimeOffset.UtcNow);
        await ProcessEventAsync(evt);
    }

    public Task<OrderProjection> GetOrderAsync() => Task.FromResult(Projection);
}

#endregion

#region Additional Event Types

public abstract record OrderEvent(string OrderId, DateTimeOffset Timestamp);
public sealed record OrderCreated(string OrderId, string CustomerId, DateTimeOffset Timestamp) : OrderEvent(OrderId, Timestamp);
public sealed record OrderItemAdded(string OrderId, string ProductId, int Quantity, decimal UnitPrice, DateTimeOffset Timestamp) : OrderEvent(OrderId, Timestamp);
public sealed record OrderShipped(string OrderId, string TrackingNumber, DateTimeOffset Timestamp) : OrderEvent(OrderId, Timestamp);

public abstract record OrderOutboxEvent(string EventId, DateTimeOffset Timestamp);
public sealed record OrderNotificationRequested(string EventId, string OrderId, string CustomerId, string Message, DateTimeOffset Timestamp) : OrderOutboxEvent(EventId, Timestamp);
public sealed record InventoryReservationRequested(string EventId, string ProductId, int Quantity, string OrderId, DateTimeOffset Timestamp) : OrderOutboxEvent(EventId, Timestamp);

public sealed record OrderProjection(
    string OrderId,
    string CustomerId,
    OrderStatus Status,
    List<OrderItem> Items,
    decimal TotalAmount,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastModifiedAt) : IEventProjection<OrderProjection>
{
    public static OrderProjection CreateDefault() => new(
        string.Empty,
        string.Empty,
        OrderStatus.Unknown,
        new List<OrderItem>(),
        0m,
        DateTimeOffset.MinValue,
        DateTimeOffset.MinValue);
}

public enum OrderStatus { Unknown, Created, Processing, Shipped, Delivered, Cancelled }
public sealed record OrderItem(string ProductId, int Quantity, decimal UnitPrice);

public abstract record TestEvent(DateTimeOffset Timestamp);
public abstract record TestOutboxEvent(string EventId, DateTimeOffset Timestamp);

public sealed record UserAnalyticsEvent(
    string EventId, 
    string UserId, 
    string EventType, 
    Dictionary<string, object> Properties, 
    DateTimeOffset Timestamp) : UserOutboxEvent(EventId, Timestamp);

#endregion

#region Service Interfaces

public interface IEmailService
{
    Task SendEmailAsync(string to, string subject, string body);
}

public class EmailService : IEmailService
{
    public async Task SendEmailAsync(string to, string subject, string body)
    {
        // Mock implementation
        await Task.Delay(100);
        Console.WriteLine($"Email sent to {to}: {subject}");
    }
}

#endregion
