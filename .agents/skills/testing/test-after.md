# Test-after approach

Validation cycle for writing tests **AFTER** production code already exists. Ensures every new test actually tests something.

> This is **not** TDD. For new code, use [tdd-workflow.md](tdd-workflow.md). The reversed-assertion step below is unnecessary in TDD because the production code is missing — the test will naturally fail for the right reason.

## Workflow

1. **Add one test at a time** — write a single test with intentionally reversed/incorrect assertions.
2. **Run the test (RED)** — verify it fails for the expected reason (the assertion is wrong, not a code error).
3. **Fix the assertion (GREEN)** — update the assertion to match the correct expected behavior.
4. **Run the test again** — confirm it passes.
5. **If still failing** — if the test fails after fixing the assertion and the test logic appears correct, **STOP and ask for instructions** (likely a bug in production code).
6. **Repeat** — continue with the next test.

## Example

```csharp
// Step 1: Write test with reversed assertion
[Fact]
public async Task Get_raw_prices_returns_data()
{
    // Arrange & Act
    var result = await repository.GetRawPricesAsync(...);

    // Assert - REVERSED FOR RED TEST
    Assert.Empty(result);  // Wrong on purpose - we expect data
}

// Step 2: Run test - should fail with "collection was not empty"

// Step 3: Fix assertion
Assert.Single(result);  // Now correct

// Step 4: Run test - should pass
```

## Why

- Ensures the test actually tests something (prevents false positives from tests that always pass).
- Validates assertion logic before committing.
- Catches production code bugs early — an unexpected RED-phase failure is a signal.
- Provides confidence that the test would catch regressions.

## Don't

- Don't batch many test bodies first and then fix all assertions — same horizontal-slice failure mode as in [tdd-workflow.md](tdd-workflow.md).
- Don't use this cycle for new code; the missing production already gives you the RED.
