using FindjobnuService.DTOs.Responses;
using FindjobnuService.Mappers;
using FindjobnuService.Repositories.Context;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SharedInfrastructure.Cities;
namespace FindjobnuService.Endpoints;

public static class CitiesEndpoints
{
    public static void MapCitiesEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/Cities").WithTags(nameof(City));

        group.MapGet("/", async (FindjobnuContext db) =>
        {
            var cities = await db.Cities
                .OrderBy(c => c.Name)
                .ToListAsync();
            return cities.Select(CitiesMapper.ToDto).ToList();
        })
        .WithName("GetAllCities");

        group.MapGet("/{id}", async Task<Results<Ok<CityResponse>, NoContent>> (int id, FindjobnuContext db) =>
        {
            try
            {
                var model = await db.Cities.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id);
                return model is City c
                    ? TypedResults.Ok(CitiesMapper.ToDto(c))
                    : TypedResults.NoContent();
            }
            catch
            {
                return TypedResults.NoContent();
            }
        })
        .WithName("GetCitiesById");

        group.MapGet("/search", async Task<Results<Ok<List<CityResponse>>, NoContent>> (string query, FindjobnuContext db) =>
        {
            if (string.IsNullOrWhiteSpace(query))
                return TypedResults.NoContent();

            var normalizedQuery = $"%{query.Trim()}%";
            var results = await db.Cities.AsNoTracking()
                .Where(model => EF.Functions.Like(model.Name, normalizedQuery))
                .OrderBy(model => model.Name)
                .ToListAsync();

            var dtos = results.Select(CitiesMapper.ToDto).ToList();
            return dtos.Any()
                ? TypedResults.Ok(dtos)
                : TypedResults.NoContent();
        })
        .WithName("GetCitiesByQuery");
    }
}
