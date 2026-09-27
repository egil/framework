namespace Egil.Orleans.Messaging.Tests.Outboxes;

public sealed class PostmanHandlerTests(MessagingTestClusterFixture fixture) : IClassFixture<MessagingTestClusterFixture>
{
    [Theory]
    [InlineData("payload-task", false, false, false)]
    [InlineData("payload-valuetask", false, false, false)]
    [InlineData("payload-async", false, false, false)]
    [InlineData("token-task", true, false, false)]
    [InlineData("token-valuetask", true, false, false)]
    [InlineData("token-async", true, false, false)]
    [InlineData("factory-task", false, true, false)]
    [InlineData("factory-valuetask", false, true, false)]
    [InlineData("factory-async", false, true, false)]
    [InlineData("cancellation-task", false, false, true)]
    [InlineData("cancellation-valuetask", false, false, true)]
    [InlineData("cancellation-async", false, false, true)]
    [InlineData("token-factory-task", true, true, false)]
    [InlineData("token-factory-valuetask", true, true, false)]
    [InlineData("token-factory-async", true, true, false)]
    [InlineData("token-cancellation-task", true, false, true)]
    [InlineData("token-cancellation-valuetask", true, false, true)]
    [InlineData("token-cancellation-async", true, false, true)]
    [InlineData("factory-cancellation-task", false, true, true)]
    [InlineData("factory-cancellation-valuetask", false, true, true)]
    [InlineData("factory-cancellation-async", false, true, true)]
    [InlineData("full-task", true, true, true)]
    [InlineData("full-valuetask", true, true, true)]
    [InlineData("full-async", true, true, true)]
    public async Task Handlers_receive_requested_context_deliver_and_acknowledge(
        string handler, bool receivesToken, bool receivesFactory, bool receivesCancellation)
    {
        var key = Guid.NewGuid();
        var source = fixture.GrainFactory.GetGrain<IPostmanHandlerSourceGrain>(key);
        var target = fixture.GrainFactory.GetGrain<IOutboxProcessorGrainPostmanTargetGrain>(key);

        var result = await source.PublishAsync(handler);
        var delivered = await target.GetStateAsync();

        Assert.Equal(handler, Assert.Single(delivered.ReceivedValues));
        Assert.Equal(receivesToken ? result.ExpectedToken : null, result.Token);
        Assert.Equal(receivesFactory, result.ReceivedFactory);
        Assert.Equal(receivesCancellation, result.CanBeCanceled);
        Assert.Equal(0, result.PendingCount);
    }

    [Theory]
    [InlineData("two-task", "token")]
    [InlineData("two-async", "token")]
    [InlineData("two-without-token-task", "factory")]
    [InlineData("two-without-token-async", "factory")]
    [InlineData("three-task", "token/cancellation")]
    [InlineData("three-async", "token/cancellation")]
    [InlineData("three-without-token-cancellation-task", "token/factory")]
    [InlineData("three-without-token-cancellation-async", "token/factory")]
    public async Task Ambiguous_handlers_use_the_documented_context_preference(string handler, string expectedShape)
    {
        var source = fixture.GrainFactory.GetGrain<IPostmanHandlerSourceGrain>(Guid.NewGuid());

        var shape = await source.ObservePreferredContextAsync(handler);

        Assert.Equal(expectedShape, shape);
    }

    [Theory]
    [InlineData("cancellation-task")]
    [InlineData("cancellation-valuetask")]
    [InlineData("full-task")]
    [InlineData("full-valuetask")]
    public async Task Caller_cancellation_reaches_the_handler_and_leaves_the_message_pending(string handler)
    {
        var source = fixture.GrainFactory.GetGrain<IPostmanHandlerSourceGrain>(Guid.NewGuid());

        var result = await source.CancelDuringDeliveryAsync(handler);

        Assert.True(result.Cancelled);
        Assert.Equal(1, result.PendingCount);
        Assert.Equal(0, result.AcknowledgedCount);
    }
}

public interface IPostmanHandlerSourceGrain : IGrainWithGuidKey
{
    Task<PostmanHandlerResult> PublishAsync(string handler);
    Task<string> ObservePreferredContextAsync(string handler);
    Task<(bool Cancelled, int PendingCount, int AcknowledgedCount)> CancelDuringDeliveryAsync(string handler);
}

[GenerateSerializer]
public sealed record PostmanHandlerResult(
    [property: Id(0)] OutboxSequenceToken ExpectedToken,
    [property: Id(1)] OutboxSequenceToken? Token,
    [property: Id(2)] bool ReceivedFactory,
    [property: Id(3)] bool CanBeCanceled,
    [property: Id(4)] int PendingCount);

public sealed class PostmanHandlerSourceGrain : Grain, IPostmanHandlerSourceGrain, IOutboxGrain
{
    private Outbox<object> outbox = [];
    private OutboxSequenceToken? receivedToken;
    private bool receivedFactory;
    private bool canBeCanceled;
    private string observedShape = "";
    private int acknowledgedCount;

    public async Task<PostmanHandlerResult> PublishAsync(string handler)
    {
        outbox = outbox.Add(new OutboxProcessorTestEvent(handler));
        var expectedToken = outbox.Envelopes[0].Id.ForSender(this.GetGrainId());
        var processor = RegisterHandler(CreateProcessor(), handler)
            .AddPostman<object>(_ => throw new InvalidOperationException("The payload handler should match first."));

        await processor.PostAsync();

        return new(expectedToken, receivedToken, receivedFactory, canBeCanceled, outbox.Count);
    }

    private OutboxProcessor<object> CreateProcessor() =>
        this.RegisterOutboxProcessor(() => outbox, options =>
        {
            options.AcknowledgePosted = items =>
            {
                acknowledgedCount += items.Length;
                outbox = outbox.RemoveRange(items);
            };
        });

    private OutboxProcessor<object> RegisterHandler(OutboxProcessor<object> processor, string handler) => handler switch
    {
        "payload-task" => processor.AddPostman<OutboxProcessorTestEvent>(PayloadTask),
        "payload-valuetask" => processor.AddPostman<OutboxProcessorTestEvent>(PayloadValueTask),
        "payload-async" => processor.AddPostman<OutboxProcessorTestEvent>(async message => await PayloadTask(message)),
        "token-task" => processor.AddPostman<OutboxProcessorTestEvent>(TokenTask),
        "token-valuetask" => processor.AddPostman<OutboxProcessorTestEvent>(TokenValueTask),
        "token-async" => processor.AddPostman<OutboxProcessorTestEvent>(async (message, token) => await TokenTask(message, token)),
        "factory-task" => processor.AddPostman<OutboxProcessorTestEvent>(FactoryTask),
        "factory-valuetask" => processor.AddPostman<OutboxProcessorTestEvent>(FactoryValueTask),
        "factory-async" => processor.AddPostman<OutboxProcessorTestEvent>(async (message, grains) => await FactoryTask(message, grains)),
        "cancellation-task" => processor.AddPostman<OutboxProcessorTestEvent>(CancellationTask),
        "cancellation-valuetask" => processor.AddPostman<OutboxProcessorTestEvent>(CancellationValueTask),
        "cancellation-async" => processor.AddPostman<OutboxProcessorTestEvent>(async (message, ct) => await CancellationTask(message, ct)),
        "token-factory-task" => processor.AddPostman<OutboxProcessorTestEvent>(TokenFactoryTask),
        "token-factory-valuetask" => processor.AddPostman<OutboxProcessorTestEvent>(TokenFactoryValueTask),
        "token-factory-async" => processor.AddPostman<OutboxProcessorTestEvent>(async (message, token, grains) => await TokenFactoryTask(message, token, grains)),
        "token-cancellation-task" => processor.AddPostman<OutboxProcessorTestEvent>(TokenCancellationTask),
        "token-cancellation-valuetask" => processor.AddPostman<OutboxProcessorTestEvent>(TokenCancellationValueTask),
        "token-cancellation-async" => processor.AddPostman<OutboxProcessorTestEvent>(async (message, token, ct) => await TokenCancellationTask(message, token, ct)),
        "factory-cancellation-task" => processor.AddPostman<OutboxProcessorTestEvent>(FactoryCancellationTask),
        "factory-cancellation-valuetask" => processor.AddPostman<OutboxProcessorTestEvent>(FactoryCancellationValueTask),
        "factory-cancellation-async" => processor.AddPostman<OutboxProcessorTestEvent>(async (message, grains, ct) => await FactoryCancellationTask(message, grains, ct)),
        "full-task" => processor.AddPostman<OutboxProcessorTestEvent>(FullTask),
        "full-valuetask" => processor.AddPostman<OutboxProcessorTestEvent>(FullValueTask),
        "full-async" => processor.AddPostman<OutboxProcessorTestEvent>(async (message, token, grains, ct) => await FullTask(message, token, grains, ct)),
        _ => throw new ArgumentOutOfRangeException(nameof(handler)),
    };

    private Task DeliverAsync(
        OutboxProcessorTestEvent message, OutboxSequenceToken? token = null,
        IGrainFactory? grains = null, CancellationToken ct = default)
    {
        receivedToken = token;
        receivedFactory = grains is not null;
        canBeCanceled = ct.CanBeCanceled;
        ct.ThrowIfCancellationRequested();
        return (grains ?? GrainFactory).GetGrain<IOutboxProcessorGrainPostmanTargetGrain>(this.GetPrimaryKey())
            .ReceiveAsync(message.Value);
    }

    private Task PayloadTask(OutboxProcessorTestEvent message) =>
        DeliverAsync(message);

    private ValueTask PayloadValueTask(OutboxProcessorTestEvent message) =>
        new(PayloadTask(message));

    private Task TokenTask(OutboxProcessorTestEvent message, OutboxSequenceToken token) =>
        DeliverAsync(message, token: token);

    private ValueTask TokenValueTask(OutboxProcessorTestEvent message, OutboxSequenceToken token) =>
        new(TokenTask(message, token));

    private Task FactoryTask(OutboxProcessorTestEvent message, IGrainFactory grains) =>
        DeliverAsync(message, grains: grains);

    private ValueTask FactoryValueTask(OutboxProcessorTestEvent message, IGrainFactory grains) =>
        new(FactoryTask(message, grains));

    private Task CancellationTask(OutboxProcessorTestEvent message, CancellationToken ct) =>
        DeliverAsync(message, ct: ct);

    private ValueTask CancellationValueTask(OutboxProcessorTestEvent message, CancellationToken ct) =>
        new(CancellationTask(message, ct));

    private Task TokenFactoryTask(OutboxProcessorTestEvent message, OutboxSequenceToken token, IGrainFactory grains) =>
        DeliverAsync(message, token: token, grains: grains);

    private ValueTask TokenFactoryValueTask(OutboxProcessorTestEvent message, OutboxSequenceToken token, IGrainFactory grains) =>
        new(TokenFactoryTask(message, token, grains));

    private Task TokenCancellationTask(OutboxProcessorTestEvent message, OutboxSequenceToken token, CancellationToken ct) =>
        DeliverAsync(message, token: token, ct: ct);

    private ValueTask TokenCancellationValueTask(OutboxProcessorTestEvent message, OutboxSequenceToken token, CancellationToken ct) =>
        new(TokenCancellationTask(message, token, ct));

    private Task FactoryCancellationTask(OutboxProcessorTestEvent message, IGrainFactory grains, CancellationToken ct) =>
        DeliverAsync(message, grains: grains, ct: ct);

    private ValueTask FactoryCancellationValueTask(OutboxProcessorTestEvent message, IGrainFactory grains, CancellationToken ct) =>
        new(FactoryCancellationTask(message, grains, ct));

    private Task FullTask(OutboxProcessorTestEvent message, OutboxSequenceToken token, IGrainFactory grains, CancellationToken ct) =>
        DeliverAsync(message, token: token, grains: grains, ct: ct);

    private ValueTask FullValueTask(OutboxProcessorTestEvent message, OutboxSequenceToken token, IGrainFactory grains, CancellationToken ct) =>
        new(FullTask(message, token, grains, ct));

    public async Task<string> ObservePreferredContextAsync(string handler)
    {
        outbox = outbox.Add(new OutboxProcessorTestEvent(handler));
        var processor = CreateProcessor();
        _ = handler switch
        {
            "two-task" => processor.AddPostman<OutboxProcessorTestEvent>((message, context) => ObserveAsync(context)),
            "two-async" => processor.AddPostman<OutboxProcessorTestEvent>(async (message, context) => await ObserveAsync(context)),
            "two-without-token-task" => processor.AddPostman<OutboxProcessorTestEvent>((message, context) => ObserveWithoutTokenAsync(context)),
            "two-without-token-async" => processor.AddPostman<OutboxProcessorTestEvent>(async (message, context) => await ObserveWithoutTokenAsync(context)),
            "three-task" => processor.AddPostman<OutboxProcessorTestEvent>((message, first, second) => ObserveAsync(first, second)),
            "three-async" => processor.AddPostman<OutboxProcessorTestEvent>(async (message, first, second) => await ObserveAsync(first, second)),
            "three-without-token-cancellation-task" => processor.AddPostman<OutboxProcessorTestEvent>((message, first, second) => ObserveWithoutTokenCancellationAsync(first, second)),
            "three-without-token-cancellation-async" => processor.AddPostman<OutboxProcessorTestEvent>(async (message, first, second) => await ObserveWithoutTokenCancellationAsync(first, second)),
            _ => throw new ArgumentOutOfRangeException(nameof(handler)),
        };

        await processor.PostAsync();

        return observedShape;
    }

    private Task ObserveAsync(object context)
    {
        observedShape = Describe(context);
        return Task.CompletedTask;
    }

    private Task ObserveAsync(object first, object second)
    {
        observedShape = Describe(first) + "/" + Describe(second);
        return Task.CompletedTask;
    }

    private Task ObserveWithoutTokenAsync(IGrainFactory grains) => ObserveAsync(grains);
    private Task ObserveWithoutTokenAsync(CancellationToken ct) => ObserveAsync(ct);
    private Task ObserveWithoutTokenCancellationAsync(OutboxSequenceToken token, IGrainFactory grains) => ObserveAsync(token, grains);
    private Task ObserveWithoutTokenCancellationAsync(IGrainFactory grains, CancellationToken ct) => ObserveAsync(grains, ct);

    private static string Describe(object context) => context switch
    {
        OutboxSequenceToken => "token",
        IGrainFactory => "factory",
        CancellationToken => "cancellation",
        _ => throw new ArgumentException("Unexpected handler context.", nameof(context)),
    };

    public async Task<(bool Cancelled, int PendingCount, int AcknowledgedCount)> CancelDuringDeliveryAsync(string handler)
    {
        using var caller = new CancellationTokenSource();
        outbox = outbox.Add(new OutboxProcessorTestEvent(handler));
        var processor = CreateProcessor();
        async Task Cancel(CancellationToken ct)
        {
            await caller.CancelAsync();
            ct.ThrowIfCancellationRequested();
        }

        _ = handler switch
        {
            "cancellation-task" => processor.AddPostman<OutboxProcessorTestEvent>((message, ct) => Cancel(ct)),
            "cancellation-valuetask" => processor.AddPostman<OutboxProcessorTestEvent>((message, ct) => new ValueTask(Cancel(ct))),
            "full-task" => processor.AddPostman<OutboxProcessorTestEvent>((message, token, grains, ct) => Cancel(ct)),
            "full-valuetask" => processor.AddPostman<OutboxProcessorTestEvent>((message, token, grains, ct) => new ValueTask(Cancel(ct))),
            _ => throw new ArgumentOutOfRangeException(nameof(handler)),
        };

        var cancelled = false;
        try
        {
            await processor.PostAsync(caller.Token);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        var result = (cancelled, outbox.Count, acknowledgedCount);
        outbox = [];
        await processor.PostAsync();
        return result;
    }
}
