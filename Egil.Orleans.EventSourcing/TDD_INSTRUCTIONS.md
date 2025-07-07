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