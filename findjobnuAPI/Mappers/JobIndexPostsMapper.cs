using FindjobnuService.DTOs.Responses;
using FindjobnuService.Models;

namespace FindjobnuService.Mappers;

public static class JobIndexPostsMapper
{
    public static JobIndexPostResponse ToDto(JobIndexPosts model)
    {
        return new JobIndexPostResponse(
            model.JobID,
            model.JobTitle ?? string.Empty,
            model.CompanyName ?? string.Empty,
            model.JobLocation,
            model.JobUrl ?? string.Empty,
            model.Published,
            model.Categories
                .Select(c => new JobPostCategoryResponse(c.CategoryId, c.CategoryKey, c.CategoryName, c.ListingUrl, c.IsActive))
                .ToList(),
            string.IsNullOrWhiteSpace(model.JobDescription) ? null : model.JobDescription,
            model.CompanyURL,
            model.BannerImageUrl,
            model.FooterImageUrl
        );
    }

    public static PagedResponse<JobIndexPostResponse> ToPagedDto(PagedList<JobIndexPosts> paged)
        => new(
            paged.Items.Select(ToDto).ToList(),
            paged.CurrentPage,
            paged.PageSize,
            paged.TotalCount
        );
}
