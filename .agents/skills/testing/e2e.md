# End-to-end tests with `ClusterFixture`

`test/Clever.PricingEngine.End2EndTests/` runs the **real** `Clever.PricingEngine.AppHost` via `Aspire.Hosting.Testing.DistributedApplicationTestingBuilder`. Use it for tests that have to verify cluster-wide behaviour: silo membership, cluster-wide ingestion control, multi-silo reconciliation.

For grain-only tests, prefer `PricingEngineSiloFixture` ([orleans.md](orleans.md)) — it's seconds, not tens of seconds.

## Fixture: `ClusterFixture`

Single xUnit class fixture for an entire E2E test class. Builds the `AppHost` with test-friendly knobs, starts it once, and exposes:

- `CreateLoggedInClientAsync(string siloResourceName, string role)` → an `HttpClient` pointed at that silo, authenticated with the `FakeUser` Authorization header (handled by the `FakeAuth` scheme). The role string must match a role declared on at least one registered endpoint via `[Authorize(Roles = ...)]`; the resulting principal carries that single role. Use the role constants from the per-BC `Roles` classes (e.g. `Clever.PricingEngine.Silo.Endpoints.Roles.SiloAdmin`).
- `CreateSiloApiClient(string siloResourceName)` → typed `SiloApiClient` over the above HTTP client, logged in with `Roles.SiloAdmin`.
- `CreateAnonymousClientAsync(string siloResourceName)` → unauthenticated `HttpClient`.
- `StartSiloAsync(string siloResourceName)` → starts a silo that was registered with `WithExplicitStart` (i.e. one beyond `--Silos:InitialActive`) and waits for it to become healthy.

There is **no** `StopSiloAsync` / `RestartSiloAsync`. See *Aspire hard-kill* below for why.

## AppHost test knobs

The AppHost reads these via `BackendKnobs.Read(config, args)`. Pass them through `DistributedApplicationTestingBuilder.CreateAsync<TEntryPoint>(args, ct)`.

| Arg | Effect |
| --- | --- |
| `--EphemeralStorage=true` | Single Session-lifetime storage emulator; reset between fixture instances. |
| `--Silos:Max=N` | Provision N silo resources `silo1..siloN`. |
| `--Silos:InitialActive=M` | First M silos auto-start; the rest get `WithExplicitStart` for `StartSiloAsync`. |
| `--EnableExternalEventHubEmulators=true` | Spin up the CPMS / Data Platform / Watts EH emulators with deterministic resource + consumer-group names. |
| `--NoFrontend=true` | Skip the npm frontend resource (faster boot, no Node required). |
| `--DisableOrleans=true` | Run silo processes without the Orleans server (rare; for HTTP-only smoke tests). |

## Build configurations

E2E tests require the `DebugE2E` or `ReleaseE2E` configuration. These compile in `ENABLE_E2E_TESTING`, which activates:

- `FakeAuth` for `Authorization: FakeUser <role-name>`.
- Test-only silo admin endpoints such as `POST /silo/v1/admin/shutdown`.
- Test-only data seeding endpoints and Aspire resource commands, including `reset-frontend-test-data`.

Never publish `DebugE2E` / `ReleaseE2E` binaries to production environments.

## Auth in E2E tests

The AppHost is built with an E2E configuration (`DebugE2E` locally, `ReleaseE2E` in CI), which compiles in `ENABLE_E2E_TESTING`. That activates `FakeAuth` (a `PolicyScheme` with `ForwardDefaultSelector` so `[Authorize(Roles = ...)]` works), and also includes the test-only graceful-shutdown endpoint.

The `FakeUser` scheme accepts `Authorization: FakeUser <role-name>`. On first use the handler scans `EndpointDataSource` for every `[Authorize(Roles = ...)]` value and caches the set; an unknown role returns 401 with a descriptive message listing the known roles. The principal carries exactly one role claim — the one in the header.

Use `await fixture.CreateSiloApiClient("silo1")` for typed admin calls, or `CreateLoggedInClientAsync("silo1", Roles.SiloAdmin)` for raw HTTP. Always reference role constants from the per-BC `Roles` classes (e.g. `Clever.PricingEngine.Silo.Endpoints.Roles.SiloAdmin`, `Clever.PricingEngine.ElectricityPricing.Endpoints.Roles.ElectricityPricingRead`). Never hand-roll JWTs.

## Aspire hard-kill: there is no graceful silo stop

`IResourceBuilder<T>.WithCommand("stop")` and the Aspire CLI both terminate the silo process. They do **not** invoke a clean Orleans `SiloHost.StopAsync()`, so the silo doesn't deregister from the membership table or drain grain activations. The cluster ends up in a degraded state that the next test inherits.

We tried two workarounds before settling on the current rule:

1. A test-only `POST /silo/v1/admin/shutdown` endpoint (gated `#if ENABLE_E2E_TESTING`) that calls `IHostApplicationLifetime.StopApplication()` after a 100 ms delay so the 202 flushes. The endpoint shuts the host down cleanly, but Aspire DCP doesn't reliably observe a host-driven exit and the test hangs in the resource-state wait. **The endpoint stays in the codebase** for future operational use and for manual debugging, but tests don't depend on it.
2. Restarting silos between tests. Same problem: Aspire's restart is hard-kill + cold start, racy enough to make the suite flaky.

**Rule:** E2E tests may *start* additional silos (via `WithExplicitStart` + `StartSiloAsync`); they may **not** stop or restart silos. If you need to verify "what happens when silo X leaves", model it as "what happens when silo Y joins later" — the convergence behaviour is symmetric and easier to test.

When `ClusterFixture` is disposed at the end of the class, the whole `DistributedApplication` is torn down at once. That tear-down is allowed to be a hard-kill because nothing observes it.

## Frontend Playwright E2E

Frontend E2E tests live under `test/Clever.PricingEngine.End2EndTests/Frontend/` and use `FrontendClusterFixture`, not `ClusterFixture`.

Fixture rules:

- Run the real AppHost with the frontend enabled.
- Use `--EphemeralStorage=true`, one silo, and `--EnableExternalEventHubEmulators=true`. The external Event Hub emulators are still needed for stable silo startup in CI even when ingestion workers are disabled.
- Disable ingestion/monitoring workers explicitly for the minimum frontend harness.
- Seed deterministic frontend data through the Aspire backend resource command `reset-frontend-test-data`. If the reset has to replace persisted grain state, write the E2E storage directly and deactivate the affected grain; don't add test-only mutation APIs to domain grain interfaces. Seeded data must preserve domain invariants, including no current EVSE attached to multiple locations.
- Keep browser "action" helpers on `FrontendBrowserSession` (for example `GoToGroupsAsync`) and assertion helpers as C# extension methods on `Microsoft.Playwright.Assertions`.
- `FrontendBrowserSession.LoginAsync(backendRole, uiRoles)` injects `Authorization: FakeUser <backendRole>` for backend calls. UI roles default to the backend role; only pass extra UI roles when the scenario intentionally needs a broader UI permission set.

Local interactive run with a visible browser and slow motion:

```powershell
dotnet test test\Clever.PricingEngine.End2EndTests\Clever.PricingEngine.End2EndTests.csproj --configuration DebugE2E --filter "FullyQualifiedName~Clever.PricingEngine.End2EndTests.Frontend" -e PLAYWRIGHT_HEADLESS=false -e PLAYWRIGHT_SLOW_MO_MS=250
```

Equivalent switches:

- `PLAYWRIGHT_HEADED=true` also makes the browser visible.
- `PLAYWRIGHT_SLOW_MO=250` is accepted as an alias for `PLAYWRIGHT_SLOW_MO_MS`.

CI runs backend E2E and frontend E2E in separate `dotnet test` processes. This isolates Aspire/DCP/AppHost state; running both fixture types in the same process caused frontend silo startup failures on hosted agents.

## 2-minute test timeout

If a single E2E test runs longer than 2 minutes locally, treat it as hung and kill the process. The deterministic suite finishes in ~21–24 seconds for 12 tests; anything past 2 minutes means the cluster is stuck (usually a silo failed to come up or DCP missed a state transition). Do not "wait it out".

## File map

```
test/Clever.PricingEngine.End2EndTests/
├─ ClusterFixture.cs              ← AppHost lifecycle, CreateLoggedInClientAsync, StartSiloAsync
├─ Ingestion/
│  └─ DataIngestionApiTests.cs    ← /silo/v1/data-ingestion happy path + multi-silo
├─ HarnessSanityTests.cs          ← smoke: AppHost boots, silo1 reachable
└─ ...
```
