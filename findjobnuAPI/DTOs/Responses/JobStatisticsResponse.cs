namespace FindjobnuService.DTOs.Responses;

public record JobStatisticsResponse(
    IReadOnlyList<CategoryJobCountResponse> TopCategories,
    IReadOnlyList<CategoryJobCountResponse> TopCategoriesLastWeek,
    int TotalJobs,
    int NewJobsLastWeek,
    int NewJobsLastMonth
);
