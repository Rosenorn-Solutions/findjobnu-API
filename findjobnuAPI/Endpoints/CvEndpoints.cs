using FindjobnuService.DTOs;
using FindjobnuService.DTOs.Requests;
using FindjobnuService.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace FindjobnuService.Endpoints;

public static class CvEndpoints
{
    public static void MapCvEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/cv").WithTags("CV");

        group.MapPost("/analyze", async Task<Results<Ok<CvReadabilityResult>, BadRequest<string>>> (
            [FromForm] UploadCvRequest request,
            [FromServices] ICvService service,
            CancellationToken ct) =>
        {
            try
            {
                var file = request.File;
                if (file == null || file.Length == 0)
                {
                    return TypedResults.BadRequest("No file uploaded.");
                }

                // Quick endpoint-level file size guard consistent with service
                const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB
                if (file.Length > MaxFileSizeBytes)
                {
                    return TypedResults.BadRequest("File too large. Max allowed size is 10 MB.");
                }

                var result = await service.AnalyzeAsync(file, ct);
                return TypedResults.Ok(result);
            }
            catch (ArgumentException ex)
            {
                return TypedResults.BadRequest(ex.Message);
            }
        })
        .Accepts<UploadCvRequest>("multipart/form-data")
        .Produces<CvReadabilityResult>(StatusCodes.Status200OK)
        .Produces<string>(StatusCodes.Status400BadRequest)
        .WithName("AnalyzeCvPdf")
        .DisableAntiforgery();

        group.MapPost("/import", async Task<Results<Ok<CvImportResult>, BadRequest<string>, UnauthorizedHttpResult>> (
            [FromForm] UploadCvRequest request,
            HttpContext ctx,
            [FromServices] ICvService service,
            CancellationToken ct) =>
        {
            var userId = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return TypedResults.Unauthorized();
            }

            try
            {
                var file = request.File;
                if (file == null || file.Length == 0)
                {
                    return TypedResults.BadRequest("No file uploaded.");
                }

                const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB
                if (file.Length > MaxFileSizeBytes)
                {
                    return TypedResults.BadRequest("File too large. Max allowed size is 10 MB.");
                }

                var result = await service.ImportToProfileAsync(userId, file, ct);
                return TypedResults.Ok(result);
            }
            catch (ArgumentException ex)
            {
                return TypedResults.BadRequest(ex.Message);
            }
        })
        .Accepts<UploadCvRequest>("multipart/form-data")
        .Produces<CvImportResult>(StatusCodes.Status200OK)
        .Produces<string>(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .RequireAuthorization()
        .WithName("ImportCvIntoProfile")
        .DisableAntiforgery();
    }
}
