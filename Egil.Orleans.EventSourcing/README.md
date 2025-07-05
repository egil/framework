# Orleans Event Sourcing with Azure Table Storage

## Implemented Functionality

### Basic EventGrain Infrastructure ✅
- **EventGrain creation and initialization**: EventGrain can be created with proper projection initialization
- **Event storage dependency injection**: EventGrain properly accepts and stores IEventStorage dependency
- **Projection initialization on grain activation**: EventGrain loads projection from storage on activation, falls back to default if none exists

### Internal Architecture ✅
- **ProjectionLoader**: Internal service for loading/saving projections from storage
- **Error handling**: Graceful fallback for unit testing scenarios where Orleans runtime context is unavailable

## EventGrain behavior on activation:

1. Load TProjection from storage, and if TProjection is out of date (all events have not been applied), then missing events should be loaded and applied.
2. If TProjection fails to deserialize from storage or is missing completely, all events are loaded from storage and passed to event handlers. Uppon completion TProjection is saved asynchronusly.
3. Detect events that needs to be published which have not been published. If they are not published, trigger publishing in the background.

