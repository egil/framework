# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Essential Commands

### Build and Test
```bash
# Build the solution
dotnet build

# Run all tests
dotnet test

# Run a specific test
dotnet test --filter "FullyQualifiedName~<TestClassName>.<TestMethodName>"

# Pack NuGet package
dotnet pack src/Egil.Orleans.EventSourcing/Egil.Orleans.EventSourcing.csproj -c Release

# Start test infrastructure (Azure Storage Emulator via Aspire)
dotnet run --project test/Egil.Orleans.EventSourcing.Tests.AppHost/
```

## Architecture Overview

This is an event sourcing library for Orleans that uses **unified storage** - both events and projections are stored in Azure Table Storage within the same transaction for atomic consistency.

### Core Components

1. **EventGrain<TEventGrain, TProjection>**: Base class for event-sourced grains. All grains inherit from this and define their events, handlers, and reactors.

2. **IEventStore<TProjection>**: Central interface implemented by `AzureTableEventStore<TProjection>`. Handles all event storage, projection updates, and transactional guarantees.

3. **Event Processing Flow**:
   - Command → Create Event(s) → Apply Handlers (update projection) → Save atomically → Execute Reactors (side effects)
   - If any handler fails, the entire operation rolls back

4. **Event Streams**: Events are organized into streams with configurable retention policies:
   - `KeepForever`: Never delete events
   - `KeepUntilReactedSuccessfully`: Delete after successful reactor execution
   - `KeepLatestDistinct`: Keep only the latest event per distinct key

### Key Design Patterns

- **Immutable Projections**: Projections are records that get replaced, never mutated
- **Pure Event Handlers**: Functions that take (projection, event) and return new projection
- **Async Event Reactors**: Handle side effects after successful event storage
- **Builder Pattern**: Fluent API for configuration (`.WithEventStream()`, `.WithHandler()`, `.WithReactor()`)

### Azure Table Storage Structure

- **Partition Key**: Grain ID (ensures all grain data in same partition)
- **Row Keys**:
  - Events: `E-{StreamName}-{SequenceNumber:D20}`
  - Projection: `P-{ProjectionName}`
  - Metadata: `M-{MetadataType}`

### Transaction Limits

Azure Table Storage limits transactions to 100 operations. The library respects this by batching operations appropriately.

## Development Guidelines

1. **Test-Driven Development**: Follow TDD_INSTRUCTIONS.md for the 8-step TDD workflow
2. **Conventional Commits**: Use format like `feat:`, `fix:`, `test:`, `refactor:`
3. **Immutability**: Use records for events and projections
4. **Nullable References**: All code must handle nullability correctly

## Testing Approach

- Unit tests use in-memory Azure Storage emulator via .NET Aspire
- Integration tests use Orleans TestingHost
- Tests follow naming convention: `MethodName_Scenario_ExpectedBehavior`
- Use `AzureTableEventStoreTests` as reference for testing patterns

### Test Method Best Practices
- **Test methods should return `void` if they contain no async code** - avoid returning `Task.CompletedTask`
- Only use `async Task` when the test actually awaits asynchronous operations
- **Never use try/catch blocks in tests** - use `Assert.Throws<T>()` or `Assert.ThrowsAsync<T>()` instead
- **Keep cyclomatic complexity at 1** - tests should have a linear flow without branching logic
- Each test should test one specific scenario without conditional logic

### Test Structure - AAA Pattern
Tests should follow the **Arrange, Act, Assert** pattern:

**For simple tests**: Use implicit sections separated by single blank lines:
```csharp
[Fact]
public void Simple_test_example()
{
    var input = "test";
    var sut = new MyClass();
    
    var result = sut.Process(input);
    
    Assert.Equal("expected", result);
}
```

**For complex tests**: Use explicit section comments when sections are large or need internal blank lines:
```csharp
[Fact]
public async Task Complex_test_with_multiple_steps()
{
    // Arrange
    var grainId = RandomGrainId();
    var sut = CreateSut();
    
    sut.Configure(grainId, new DummyGrain(), fixture.Services,
        builder => builder
            .AddStream<IEvent>()
            .Handle<StrEvent>((evt, pro) => pro));
    await sut.InitializeAsync();
    
    sut.AppendEvent(new StrEvent("Event1"));
    sut.AppendEvent(new StrEvent("Event2"));
    
    // Act
    await sut.ReactEventsAsync(context);
    var events = await sut.GetEventsAsync<StrEvent>().ToListAsync();
    
    await sut.CommitAsync();
    
    // Assert
    Assert.Equal(2, events.Count);
    Assert.True(sut.HasUnreactedEvents);
}
```

## Common Development Tasks

### Adding a New Event Type
1. Define event as a record in your grain
2. Add handler via `.WithHandler<TEvent>()` 
3. Optionally add reactor via `.WithReactor<TEvent>()`
4. Write tests first following TDD approach

### Debugging Event Processing
1. Check event sequence in Azure Table Storage Explorer
2. Use `GetEventsAsync()` to inspect stored events
3. Verify projection state matches expected handler results
4. Check reactor execution status in metadata

## CI/CD Pipeline

GitHub Actions workflow (`.github/workflows/egil-orleans-eventsourcing-ci.yml`):
- Builds and tests on every push
- Creates NuGet packages on main branch
- Publishes to NuGet.org on release branches
- Uses Nerdbank.GitVersioning for semantic versioning