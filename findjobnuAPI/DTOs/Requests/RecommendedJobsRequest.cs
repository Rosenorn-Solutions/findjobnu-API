namespace FindjobnuService.DTOs.Requests;

using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

public record RecommendedJobsRequest(
    [property: FromQuery(Name = "SearchTerms")] string[]? SearchTerms,
    [property: FromQuery(Name = "Locations")] string[]? Locations,
    [property: FromQuery(Name = "CategoryKeys")] string[]? CategoryKeys,
    [property: FromQuery] DateTime? PostedAfter,
    [property: FromQuery] DateTime? PostedBefore,
    [property: FromQuery, Range(1, int.MaxValue)] int Page = 1,
    [property: FromQuery, Range(1, 200)] int PageSize = 20
);
