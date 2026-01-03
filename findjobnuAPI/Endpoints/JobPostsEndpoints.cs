using FindjobnuService.DTOs.Requests;
using FindjobnuService.DTOs.Responses;
using FindjobnuService.Mappers;
using FindjobnuService.Models;
using FindjobnuService.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace FindjobnuService.Endpoints;

public static class JobPostsEndpoints
{
    public static void MapJobIndexPostsEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/jobindexposts").WithTags(nameof(JobIndexPosts));

        group.MapGet("/", async Task<Results<Ok<PagedResponse<JobIndexPostResponse>>, BadRequest<string>, NoContent>> (
            [FromServices] IJobIndexPostsService service,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10) =>
        {
            if (page < 1 || pageSize < 1 || pageSize > 200)
                return TypedResults.BadRequest("Invalid paging parameters.");
            try
            {
                var pagedList = await service.GetAllAsync(page, pageSize);
                var dto = JobIndexPostsMapper.ToPagedDto(pagedList);

                if (dto.Items.Any())
                {
                    routes.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("CacheHeaders");
                }
                return (dto?.Items?.Any() == true) ? TypedResults.Ok(dto) : TypedResults.NoContent();
            }
            catch
            {
                return TypedResults.NoContent();
            }
        })
        .WithName("GetAllJobPosts")
        .CacheOutput(p => p.Expire(TimeSpan.FromMinutes(2)));

        group.MapGet("/search", async Task<Results<Ok<PagedResponse<JobIndexPostResponse>>, BadRequest<string>, NoContent>> (
            [AsParameters] JobIndexPostsSearchRequest request,
            [FromServices] IJobIndexPostsService service) =>
        {
            if (request.Page < 1 || request.PageSize < 1 || request.PageSize > 200)
                return TypedResults.BadRequest("Invalid paging parameters.");
            try
            {
                var pagedList = await service.SearchAsync(
                    request.SearchTerm,
                    request.Location,
                    request.CategoryId,
                    request.PostedAfter,
                    request.PostedBefore,
                    request.Page,
                    request.PageSize);

                var dto = JobIndexPostsMapper.ToPagedDto(pagedList);
                return (dto?.Items?.Any() == true) ? TypedResults.Ok(dto) : TypedResults.NoContent();
            }
            catch
            {
                return TypedResults.NoContent();
            }
        })
        .WithName("GetJobPostsBySearch");

        group.MapGet("/{id}", async Task<Results<Ok<JobIndexPostResponse>, BadRequest<string>, NoContent>> (int id, [FromServices] IJobIndexPostsService service) =>
        {
            if (id <= 0)
                return TypedResults.BadRequest("Invalid id.");
            try
            {
                var jobPost = await service.GetByIdAsync(id);
                if (jobPost == null || string.IsNullOrEmpty(jobPost.JobUrl))
                    return TypedResults.NoContent();
                return TypedResults.Ok(JobIndexPostsMapper.ToDto(jobPost));
            }
            catch
            {
                return TypedResults.NoContent();
            }
        })
        .WithName("GetJobPostsById");

        group.MapGet("/categories", async Task<Results<Ok<CategoriesResponse>, NoContent>> ([FromServices] IJobIndexPostsService service) =>
        {
            var categories = await service.GetCategoriesAsync();
            return categories.Categories.Count > 0 ? TypedResults.Ok(categories) : TypedResults.NoContent();
        })
        .WithName("GetJobCategories");

        group.MapGet("/statistics", async Task<Results<Ok<JobStatisticsResponse>, NoContent>> ([FromServices] IJobIndexPostsService service) =>
        {
            try
            {
                var stats = await service.GetStatisticsAsync();
                return TypedResults.Ok(stats);
            }
            catch
            {
                return TypedResults.NoContent();
            }
        })
        .WithName("GetJobStatistics")
        .CacheOutput(p => p.Expire(TimeSpan.FromMinutes(5)));

        group.MapGet("/saved", async Task<Results<Ok<PagedResponse<JobIndexPostResponse>>, BadRequest<string>, UnauthorizedHttpResult, NoContent>> (
            [FromQuery] int page,
            HttpContext httpContext,
            [FromServices] IJobIndexPostsService service) =>
        {
            if (page < 1)
                return TypedResults.BadRequest("Invalid page.");

            var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return TypedResults.Unauthorized();

            var pagedList = await service.GetSavedJobsByUserId(userId, page);
            var dto = JobIndexPostsMapper.ToPagedDto(pagedList!);
            return dto.Items.Any() ? TypedResults.Ok(dto) :
                TypedResults.NoContent();
        })
        .RequireAuthorization()
        .WithName("GetSavedJobPostsByUser");

        group.MapGet("/recommended-jobs", async Task<Results<Ok<PagedResponse<JobIndexPostResponse>>, UnauthorizedHttpResult, BadRequest<string>, NoContent>> (
            [AsParameters] RecommendedJobsRequest request,
            HttpContext httpContext,
            [FromServices] IJobIndexPostsService jobService) =>
        {
            var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return TypedResults.Unauthorized();
            if (request == null)
                return TypedResults.BadRequest("Invalid request parameters.");

            var pagedList = await jobService.GetRecommendedJobsByUserAndProfile(userId, request);
            var dto = JobIndexPostsMapper.ToPagedDto(pagedList!);
            return dto.Items.Any() ? TypedResults.Ok(dto) :
                TypedResults.NoContent();
        })
        .RequireAuthorization()
        .WithName("GetRecommendedJobsForUser");
    }
}
