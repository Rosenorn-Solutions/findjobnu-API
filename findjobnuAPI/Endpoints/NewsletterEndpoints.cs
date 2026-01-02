using FindjobnuService.DTOs.Requests;
using FindjobnuService.DTOs.Responses;
using FindjobnuService.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace FindjobnuService.Endpoints
{
    public static class NewsletterEndpoints
    {
        public static void MapNewsletterEndpoints(this IEndpointRouteBuilder routes)
        {
            var group = routes.MapGroup("/api/newsletter").WithTags("Newsletter");

            group.MapPost("/subscribe", async Task<Results<Ok<NewsletterSubscribeResponse>, BadRequest<string>>> (
                [FromBody] NewsletterSubscribeRequest request,
                [FromServices] INewsletterService service) =>
            {
                if (request == null || string.IsNullOrWhiteSpace(request.Email))
                    return TypedResults.BadRequest("Email is required.");

                try
                {
                    var result = await service.SubscribeAsync(request.Email);
                    return TypedResults.Ok(result);
                }
                catch (ArgumentException ex)
                {
                    return TypedResults.BadRequest(ex.Message);
                }
            })
            .WithName("SubscribeNewsletter");
        }
    }
}
