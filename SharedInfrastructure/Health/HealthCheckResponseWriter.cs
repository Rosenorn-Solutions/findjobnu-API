using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace SharedInfrastructure.Health;

public sealed class ApplicationHealthMetadata
{
    public ApplicationHealthMetadata(DateTimeOffset startedAt)
    {
        StartedAt = startedAt;
    }

    public DateTimeOffset StartedAt { get; }
}

public static class HealthCheckResponseWriter
{
    public static Task WriteDetailedResponse(HttpContext context, HealthReport report)
    {
        var metadata = context.RequestServices.GetService<ApplicationHealthMetadata>();
        var startedAt = metadata?.StartedAt;
        var environment = context.RequestServices.GetService<IHostEnvironment>();

        ThreadPool.GetAvailableThreads(out var availableWorkers, out var availableIo);
        ThreadPool.GetMaxThreads(out var maxWorkers, out var maxIo);

        var payload = new
        {
            status = report.Status.ToString(),
            environment = environment?.EnvironmentName,
            startedAt,
            uptimeSeconds = startedAt.HasValue ? (double?)(DateTimeOffset.UtcNow - startedAt.Value).TotalSeconds : null,
            entries = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                description = entry.Value.Description,
                durationMs = entry.Value.Duration.TotalMilliseconds,
                error = entry.Value.Exception?.Message,
                tags = entry.Value.Tags
            }),
            process = new
            {
                workingSetBytes = Environment.WorkingSet,
                gcMemoryBytes = GC.GetTotalMemory(false),
                threadPool = new
                {
                    worker = new { available = availableWorkers, max = maxWorkers },
                    io = new { available = availableIo, max = maxIo }
                }
            }
        };

        context.Response.ContentType = "application/json";
        return context.Response.WriteAsJsonAsync(payload);
    }
}
