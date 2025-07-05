# TDD Implementation Instructions for EventGrain

## Overview
This document outlines the Test-Driven Development (TDD) approach for implementing the EventGrain and related types in the Orleans Event Sourcing framework.

## TDD Process
1. **Start with the most basic scenario** that is not yet implemented
2. **Create a test** for that scenario
3. **Validate the test fails** for the correct reason (Red phase)
4. **Create minimal implementation** to make the test pass (Green phase)
5. **Consider refactoring** if needed (Refactor phase)
6. **Update README.md** with description of implemented functionality
7. **Create a git commit** for the changes
8. **Stop and review** before proceeding to the next test case

## Commits

- Follow [Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/) standard.
- Make sure to create separate commits for new features, fixes, and refactorings.

## Implementation Priority Order

### Phase 1: Basic EventGrain Infrastructure
1. **Basic EventGrain creation and initialization**
   - Test: Create a simple EventGrain with a basic projection
   - Implementation: Basic constructor and projection initialization

2. **Event storage dependency injection**
   - Test: EventGrain receives IEventStorage in constructor
   - Implementation: Proper dependency injection setup

3. **Projection initialization on grain activation**
   - Test: Projection is properly initialized on grain activation
   - Implementation: OnActivateAsync override to load projection

### Phase 2: Event Processing
4. **Basic event appending**
   - Test: ProcessEventsAsync can append a single event
   - Implementation: Basic event storage and processing

5. **Event handler execution**
   - Test: Event handlers are called when processing events
   - Implementation: Event handler registration and execution

6. **Projection updates**
   - Test: Projection is updated when events are processed
   - Implementation: Apply events to projection through handlers

### Phase 3: Event Storage and Retrieval
7. **Event persistence**
   - Test: Events are persisted to storage
   - Implementation: IEventStorage implementation for persistence

8. **Event retrieval**
   - Test: GetEventsAsync returns stored events
   - Implementation: Event loading from storage

9. **Projection loading from storage**
   - Test: Projection is loaded from storage on activation
   - Implementation: Projection serialization/deserialization

### Phase 4: Advanced Features
10. **Event publishing**
    - Test: Events marked for publishing are published
    - Implementation: Event publishing mechanism

11. **Batch event processing**
    - Test: Multiple events can be processed in a batch
    - Implementation: Batch processing optimization

12. **Event handler cascading**
    - Test: Event handlers can append new events that are also processed
    - Implementation: Cascading event processing

## Test Structure Guidelines

### Test File Organization
- Place tests in `test/Egil.Orleans.EventSourcing.Tests/`
- Use descriptive test class names ending with `Tests`
- Group related tests in the same test class

### Test Naming Convention
- Use descriptive method names that explain the scenario
- Format: `MethodUnderTest_Scenario_ExpectedBehavior`
- Example: `ProcessEventsAsync_WithSingleEvent_UpdatesProjection`

### Test Implementation Patterns
- **Arrange**: Set up test data and dependencies
- **Act**: Execute the method under test
- **Assert**: Verify the expected behavior

### Mock and Fake Usage
- Use minimal mocks/fakes - prefer real implementations when possible
- Mock external dependencies (IEventStorage, IEventHandler)
- Use test doubles for complex dependencies

## Implementation Guidelines

### Keep It Simple
- Implement the minimal code to make tests pass
- Avoid over-engineering in early phases
- Refactor only when tests are green

### Incremental Development
- Each test should build upon previous functionality
- Don't skip steps - implement in order
- Each commit should have working functionality

### Error Handling
- Start with happy path scenarios
- Add error handling tests after basic functionality works
- Handle edge cases in later phases

## Documentation Updates

### README.md Updates
After each implementation phase, update the main README.md with:
- Description of newly implemented functionality
- Usage examples if applicable
- Any breaking changes or limitations

### Code Documentation
- Add XML documentation for public APIs
- Include usage examples in complex methods
- Document any assumptions or constraints

## Git Commit Guidelines

### Commit Message Format
```
[TDD] Brief description of implemented feature

- Test: Description of the test scenario
- Implementation: Description of the implementation
- Status: All tests passing/specific test results
```

### Commit Scope
- One logical feature per commit
- Include both test and implementation in same commit
- Update documentation in the same commit when relevant

## Success Criteria for Each Phase
- All tests pass
- Code coverage is adequate for implemented features
- Documentation is updated
- No obvious code smells or design issues
- Implementation follows SOLID principles where applicable

## Next Steps After Each Review
1. Review test coverage and implementation quality
2. Consider refactoring opportunities
3. Identify next simplest scenario to implement
4. Plan next test case
5. Proceed with next TDD cycle
