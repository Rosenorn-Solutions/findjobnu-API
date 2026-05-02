namespace FindjobnuService.DTOs.Responses;

public record JobIndexPostResponse(
    long Id,
    string Title,
    string Company,
    string? Location,
    string JobUrl,
    DateTime? PostedDate,
    IReadOnlyList<JobPostCategoryResponse> Categories,
    string? Description,
    string? CompanyUrl,
    string? BannerImageUrl,
    string? FooterImageUrl
);

public record JobPostCategoryResponse(long Id, string CategoryKey, string CategoryName, string ListingUrl, bool IsActive);
