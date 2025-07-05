# Orleans Event Sourcing with Azure Table Storage

## Implemented Functionality

### Basic EventGrain Infrastructure ✅
- **EventGrain creation and initialization**: EventGrain can be created with proper projection initialization
- **Event storage dependency injection**: EventGrain properly accepts and stores IEventStorage dependency
- **Projection initialization**: Projection is initialized with default values using `TProjection.CreateDefault()`

## EventGrain behavior on activation:

1. Load TProjection from storage, and if TProjection is out of date (all events have not been applied), then missing events should be loaded and applied.
2. If TProjection fails to deserialize from storage or is missing completely, all events are loaded from storage and passed to event handlers. Uppon completion TProjection is saved asynchronusly.
3. Detect events that needs to be published which have not been published. If they are not published, trigger publishing in the background.

