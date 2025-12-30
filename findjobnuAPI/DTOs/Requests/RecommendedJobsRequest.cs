namespace FindjobnuService.DTOs.Requests;

using System.ComponentModel.DataAnnotations;

public record RecommendedJobsRequest(
    [property: MaxLength(200)] string? SearchTerm,
    [property: MaxLength(100)] string? Location,
    [property: Range(1, int.MaxValue)] int? CategoryId,
    DateTime? PostedAfter,
    DateTime? PostedBefore,
    [property: Range(1, int.MaxValue)] int Page = 1,
    [property: Range(1, 200)] int PageSize = 20
);
