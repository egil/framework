# Orleans Event Sourcing with Azure Table Storage

## Architecture Overview

This library provides event sourcing capabilities for Orleans grains using **Azure Table Storage** as the unified storage backend. The key architectural principle is **atomic transactions** - both event streams and projections are stored in Azure Table Storage, enabling atomic updates within a single transaction scope.

### Key Features:
- **Unified Storage**: Events and projections both stored in Azure Table Storage
- **Atomic Transactions**: Ensures events and projections are saved together or not at all
- **Event Partitioning**: Support for partitioned event streams
- **Projection Loading**: Automatic projection loading and caching on grain activation

## Implemented Functionality

### Basic EventGrain Infrastructure ✅
- **EventGrain creation and initialization**: EventGrain can be created with proper projection initialization
- **Event storage dependency injection**: EventGrain properly accepts and stores IEventStorage dependency
- **Projection initialization on grain activation**: EventGrain loads projection from storage on activation, falls back to default if none exists

### Storage Architecture ✅
- **IEventStorage Interface**: Unified interface for both events and projections with atomic operations
- **Projection Loading**: Load projections from Azure Table Storage
- **Event Stream Reading**: Read events from partitioned streams
- **Atomic Operations**: `SaveAtomicallyAsync` ensures transactional consistency

### Internal Architecture ✅
- **ProjectionLoader**: Internal service for loading projections from storage (delegates to IEventStorage)
- **Error handling**: Graceful fallback for unit testing scenarios where Orleans runtime context is unavailable

## EventGrain behavior on activation:

1. Load TProjection from storage, and if TProjection is out of date (all events have not been applied), then missing events should be loaded and applied.
2. If TProjection fails to deserialize from storage or is missing completely, all events are loaded from storage and passed to event handlers. Uppon completion TProjection is saved asynchronusly.
3. Detect events that needs to be published which have not been published. If they are not published, trigger publishing in the background.

