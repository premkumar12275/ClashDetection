# Clash Detection Service

A .NET 10 minimal API that detects 3D overlaps ("clashes") between buildings. Each building is a
2D GeoJSON polygon footprint extruded vertically from `elevation` to `elevation + height`. The
service returns the overlapping sections as a GeoJSON `FeatureCollection`.

## How a clash is defined

Two buildings clash when **both** conditions hold:

1. **Footprints intersect** with positive area (2D polygon intersection), and
2. **Vertical intervals overlap** — `[elevation, elevation+height]` of A and B intersect.

The resulting clash section carries:

- `geometry` — the 2D intersection of the two footprints
- `elevation` = `max(elevationA, elevationB)`
- `height` = `min(topA, topB) − elevation`
- `buildings` = the two ids that overlap

When three or more buildings share a region, **every pairwise overlap** is reported
(matching `output-sample2.json`).

## Algorithm & data structures

A naive solution compares all `n·(n−1)/2` building pairs — `O(n²)` expensive polygon intersections.
This service optimizes with:

| Concern | Approach |
|---|---|
| Candidate pruning | **STRtree (packed R-tree)** from NetTopologySuite. Building envelopes are indexed once (`O(n log n)`); querying returns only spatially-near candidates, so polygon intersection runs on a near-linear number of pairs for distributed inputs. |
| Cheap-first filtering | The **vertical interval test** (two comparisons) runs before the expensive footprint intersection, short-circuiting most non-clashing pairs. |
| Pair de-duplication | Buildings carry a stable `Index`; only pairs with `j > i` are evaluated, so each unordered pair is tested once. |
| Robustness | Invalid/self-touching polygons are repaired via `Buffer(0)`; overlay precision errors fall back to a noded retry. Zero-area "touching" intersections are filtered by a minimum-area threshold. |

Core engine: [`ClashDetector.cs`](src/ClashDetection.Api/Detection/ClashDetector.cs).

## Meeting the requirements

### Input validation (descriptive errors)
[`GeoJsonValidator`](src/ClashDetection.Api/Validation/GeoJsonValidator.cs) collects **all** problems in
one pass (type, non-empty features, unique non-empty ids, finite positive `height`, present
`elevation`, valid closed Polygon rings) and returns them as RFC 9457 ProblemDetails with `400`.

### Computations longer than the 10s client timeout
An **asynchronous request-reply** pattern decouples the HTTP request from the computation:

- `POST /api/clashes` enqueues the work onto a background queue and waits up to a short
  **sync-wait window** (default 2s, configurable). If the result is ready (cache hit or fast
  computation) it returns `200` inline; otherwise it returns **`202 Accepted`** with a `Location`
  header pointing at the polling URL.
- `GET /api/clashes/{jobId}` returns `202` while running and `200` with the result when complete.

The actual computation runs in a hosted [`ClashJobWorker`](src/ClashDetection.Api/Jobs/ClashJobWorker.cs)
(several concurrent consumers, per-job timeout), so a slow request never blocks a request thread or
trips the client's 10s timeout.

### Caching
The request body is SHA-256 hashed; the hash is both the **cache key** and the **job id**. Identical
inputs return the cached result immediately, and concurrent identical requests de-duplicate onto a
single computation. See [`MemoryClashCache`](src/ClashDetection.Api/Caching/MemoryClashCache.cs).

### Scalable to production
Every stateful collaborator sits behind an interface with an in-memory default and a documented swap
point for a distributed implementation:

| Interface | Local default | Production swap |
|---|---|---|
| `IClashCache` | `IMemoryCache` | Redis / `IDistributedCache` |
| `IClashJobStore` | `ConcurrentDictionary` | Redis / SQL |
| `IClashJobQueue` | `System.Threading.Channels` | Azure Service Bus / Redis Streams / RabbitMQ |

With those swapped, the API is stateless and horizontally scalable: any instance can accept a request,
any worker can pick up a job, and cache/job state is shared. Request bodies are capped (50 MB) to
bound memory.

## Endpoints

| Method | Route | Description |
|---|---|---|
| `POST` | `/api/clashes` | Submit buildings. `200` result, `202` accepted (poll `Location`), or `400` invalid. |
| `GET` | `/api/clashes/{jobId}` | Poll a job. `200` result, `202` processing, `404` unknown, `500` failed. |
| `GET` | `/health` | Liveness probe. |

## Running

```bash
dotnet run --project src/ClashDetection.Api

# Submit (sample input is in the repo root)
curl -X POST http://localhost:5122/api/clashes \
  -H "Content-Type: application/json" \
  --data-binary @input-sample1.json
```

Configuration (e.g. in `appsettings.json` or env var `Clash__SyncWaitWindowSeconds`):

```json
{ "Clash": { "SyncWaitWindowSeconds": 2.0 } }
```

## Tests

```bash
dotnet test
```

21 tests cover the detection engine (both samples + edge cases: touching footprints, disjoint
heights, partial vertical overlap, 3-way pairwise), validation, and the HTTP API
(`WebApplicationFactory` integration tests for the `200`/`202`/`400`/`404` paths and cache idempotency).

## Project layout

```
src/ClashDetection.Api/
  Detection/    ClashDetector (STRtree + NTS intersection), Building model
  Validation/   GeoJsonValidator
  Caching/      IClashCache + in-memory impl
  Jobs/         job model, store, channel queue, background worker, orchestrator
  Hashing/      SHA-256 content hasher (cache key / job id)
  Models/       GeoJSON DTOs
  Program.cs    DI wiring + minimal API endpoints
tests/ClashDetection.Tests/
```
