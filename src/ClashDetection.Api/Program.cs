using System.Text.Json;
using System.Text.Json.Serialization;
using ClashDetection.Api.Caching;
using ClashDetection.Api.Detection;
using ClashDetection.Api.Hashing;
using ClashDetection.Api.Jobs;
using ClashDetection.Api.Models;
using ClashDetection.Api.Validation;

var builder = WebApplication.CreateBuilder(args);

// ---- Options & shared JSON settings ------------------------------------------------------------
builder.Services.Configure<ClashOptions>(builder.Configuration.GetSection("Clash"));

var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
};

// ---- Service registrations ---------------------------------------------------------------------
// All collaborators are behind interfaces so the in-memory implementations can be swapped for
// distributed equivalents (Redis cache/store, Service Bus queue) without touching call sites.
builder.Services.AddMemoryCache(o => o.SizeLimit = 10_000);
builder.Services.AddSingleton<IClashDetector, ClashDetector>();
builder.Services.AddSingleton<IClashCache, MemoryClashCache>();
builder.Services.AddSingleton<IClashJobStore, InMemoryClashJobStore>();
builder.Services.AddSingleton<IClashJobQueue, ChannelClashJobQueue>();
builder.Services.AddSingleton<ClashJobService>();
builder.Services.AddHostedService<ClashJobWorker>();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

// Cap request bodies so a hostile/huge payload can't exhaust memory while we buffer it for hashing.
const long MaxBodyBytes = 50 * 1024 * 1024; // 50 MB

// ---- Endpoints ---------------------------------------------------------------------------------

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
   .WithName("Health");

// Submit buildings for clash detection.
//   200 → result ready (cache hit or computed within the sync-wait window)
//   202 → still computing; poll the Location URL
//   400 → invalid input (descriptive errors)
app.MapPost("/api/clashes", async (HttpRequest request, ClashJobService jobs, CancellationToken ct) =>
{
    // Read the raw body once: we need the exact bytes for the cache hash AND for deserialization.
    var body = await ReadBodyAsync(request, MaxBodyBytes, ct);
    if (body is null)
        return Results.Problem(
            title: "Payload too large",
            detail: $"Request body exceeds the {MaxBodyBytes / (1024 * 1024)} MB limit.",
            statusCode: StatusCodes.Status413PayloadTooLarge);

    FeatureCollection? input;
    try
    {
        input = JsonSerializer.Deserialize<FeatureCollection>(body, jsonOptions);
    }
    catch (JsonException ex)
    {
        return Results.Problem(
            title: "Malformed JSON",
            detail: $"Request body is not valid JSON: {ex.Message}",
            statusCode: StatusCodes.Status400BadRequest);
    }

    var errors = GeoJsonValidator.Validate(input);
    if (errors.Count > 0)
        return Results.ValidationProblem(
            new Dictionary<string, string[]> { ["input"] = errors.ToArray() },
            title: "Invalid GeoJSON input");

    var hash = InputHasher.Hash(body);
    var submission = await jobs.SubmitAsync(input!, hash, ct);

    return submission.Outcome switch
    {
        SubmissionOutcome.Completed => Results.Json(submission.Result, jsonOptions),
        SubmissionOutcome.Failed => Results.Problem(
            title: "Clash detection failed",
            detail: submission.Error,
            statusCode: StatusCodes.Status500InternalServerError),
        _ => Accepted(submission.JobId),
    };
})
.WithName("SubmitClashes");

// Poll a previously-accepted job.
//   200 → completed result
//   202 → still queued/processing
//   404 → unknown job id
//   500 → job failed
app.MapGet("/api/clashes/{jobId}", (string jobId, ClashJobService jobs) =>
{
    var job = jobs.GetJob(jobId);
    if (job is null)
        return Results.Problem(
            title: "Job not found",
            detail: $"No clash-detection job exists with id '{jobId}'.",
            statusCode: StatusCodes.Status404NotFound);

    return job.Status switch
    {
        ClashJobStatus.Completed => Results.Json(job.Result, jsonOptions),
        ClashJobStatus.Failed => Results.Problem(
            title: "Clash detection failed",
            detail: job.Error,
            statusCode: StatusCodes.Status500InternalServerError),
        _ => Accepted(job.JobId),
    };
})
.WithName("GetClashJob");

app.Run();

// ---- Local helpers -----------------------------------------------------------------------------

// 202 Accepted with a Location header and a small status envelope for polling.
static IResult Accepted(string jobId)
{
    var statusUrl = $"/api/clashes/{jobId}";
    return Results.Accepted(statusUrl, new
    {
        jobId,
        status = ClashJobStatus.Processing.ToString(),
        statusUrl,
        message = "Computation in progress. Poll the statusUrl until it returns 200.",
    });
}

// Buffer the request body up to a byte cap; returns null if the cap is exceeded.
static async Task<byte[]?> ReadBodyAsync(HttpRequest request, long maxBytes, CancellationToken ct)
{
    if (request.ContentLength is > 0 and var len && len > maxBytes)
        return null;

    using var buffer = new MemoryStream();
    var chunk = new byte[81920];
    int read;
    while ((read = await request.Body.ReadAsync(chunk, ct)) > 0)
    {
        if (buffer.Length + read > maxBytes)
            return null;
        buffer.Write(chunk, 0, read);
    }
    return buffer.ToArray();
}

// Exposed so the test host (WebApplicationFactory) can reference the entry point.
public partial class Program;
