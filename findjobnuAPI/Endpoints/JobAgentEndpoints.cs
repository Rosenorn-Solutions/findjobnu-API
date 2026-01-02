using FindjobnuService.DTOs;
using FindjobnuService.DTOs.Requests;
using FindjobnuService.Mappers;
using FindjobnuService.Models;
using FindjobnuService.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace FindjobnuService.Endpoints
{
    public static class JobAgentEndpoints
    {
        public static void MapJobAgentEndpoints(this IEndpointRouteBuilder routes)
        {
            var group = routes.MapGroup("/api/jobagent").WithTags("JobAgent").RequireAuthorization();

            group.MapGet("/{profileId}", async Task<Results<Ok<JobAgentDto>, ForbidHttpResult, NotFound>> (int profileId, HttpContext ctx, IJobAgentService service, IProfileService profiles) =>
            {
                var authedUserId = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var profile = await profiles.GetByUserIdAsync(authedUserId!);
                if (profile == null || profile.Id != profileId)
                    return TypedResults.Forbid();
                var agent = await service.GetByProfileIdAsync(profileId);
                var dto = JobAgentMapper.ToDto(agent);
                return dto != null ? TypedResults.Ok(dto) : TypedResults.NotFound();
            }).WithName("GetJobAgent");

            group.MapPost("/{profileId}", async Task<Results<Ok<JobAgentDto>, ForbidHttpResult, BadRequest<string>>> (int profileId, JobAgentUpdateRequest request, HttpContext ctx, IJobAgentService service, IProfileService profiles) =>
            {
                var authedUserId = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var profile = await profiles.GetByUserIdAsync(authedUserId!);
                if (profile == null || profile.Id != profileId)
                    return TypedResults.Forbid();

                var frequency = request.Frequency ?? JobAgentFrequency.Weekly;
                var agent = await service.CreateOrUpdateAsync(
                    profileId,
                    request.Enabled,
                    frequency,
                    request.PreferredLocations,
                    request.PreferredCategoryIds,
                    request.IncludeKeywords);
                var dto = JobAgentMapper.ToDto(agent);
                return TypedResults.Ok(dto!);
            }).WithName("CreateOrUpdateJobAgent");

            // Get unsubscribe link (for UI to display or test)
            group.MapGet("/{profileId}/unsubscribe-link", async Task<Results<Ok<string>, ForbidHttpResult, NotFound>> (int profileId, HttpContext ctx, IJobAgentService service, IProfileService profiles, [FromServices] IConfiguration config) =>
            {
                var authedUserId = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var profile = await profiles.GetByUserIdAsync(authedUserId!);
                if (profile == null || profile.Id != profileId)
                    return TypedResults.Forbid();

                var token = await service.GetOrCreateUnsubscribeTokenAsync(profileId);
                if (token == null) return TypedResults.NotFound();

                var baseUrl = config["PublicBaseUrl"]?.TrimEnd('/') ?? "https://findjob.nu";
                var link = $"{baseUrl}/api/jobagent/unsubscribe/{token}";
                return TypedResults.Ok(link);
            }).WithName("GetJobAgentUnsubscribeLink");

            // Public unsubscribe endpoint (no auth)
            routes.MapGet("/api/jobagent/unsubscribe/{token}", async Task<Results<Ok, NotFound>> (string token, IJobAgentService service) =>
            {
                var ok = await service.UnsubscribeByTokenAsync(token);
                return ok ? TypedResults.Ok() : TypedResults.NotFound();
            }).WithName("UnsubscribeJobAgent").WithTags("JobAgent");
        }
    }
}
