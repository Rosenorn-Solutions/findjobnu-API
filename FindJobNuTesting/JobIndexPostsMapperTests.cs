using FindjobnuService.Mappers;
using FindjobnuService.Models;

namespace FindjobnuTesting;

public class JobIndexPostsMapperTests
{
    [Fact]
    public void ToDto_Maps_All_Fields()
    {
        var model = new JobIndexPosts
        {
            JobID = 1,
            JobTitle = "Dev",
            CompanyName = "ACME",
            JobLocation = "City",
            JobUrl = "http://job",
            Published = DateTime.UtcNow.Date,
            Categories = new List<Category> { new Category { Name = "IT" } }
        };

        var dto = JobIndexPostsMapper.ToDto(model);

        Assert.Equal(model.JobID, dto.Id);
        Assert.Equal(model.JobTitle, dto.Title);
        Assert.Equal(model.CompanyName, dto.Company);
        Assert.Equal(model.JobLocation, dto.Location);
        Assert.Equal(model.JobUrl, dto.JobUrl);
        Assert.Equal(model.Published, dto.PostedDate);
        Assert.Single(dto.Categories);
        Assert.Equal("IT", dto.Categories[0].CategoryName);
    }

    [Fact]
    public void ToPagedDto_Maps_Pagination_And_Items()
    {
        var list = new List<JobIndexPosts>
        {
            new JobIndexPosts { JobID = 1, JobTitle = "A", CompanyName = "X", JobUrl = "u", Published = DateTime.UtcNow },
            new JobIndexPosts { JobID = 2, JobTitle = "B", CompanyName = "Y", JobUrl = "u2", Published = DateTime.UtcNow }
        };
        var paged = new PagedList<JobIndexPosts>(totalCount: 100, pageSize: 10, currentPage: 2, items: list);

        var dto = JobIndexPostsMapper.ToPagedDto(paged);

        Assert.Equal(2, dto.Page);
        Assert.Equal(10, dto.PageSize);
        Assert.Equal(100, dto.TotalCount);
        Assert.Equal(2, dto.Items.Count);
        Assert.Contains(dto.Items, x => x.Id == 1);
        Assert.Contains(dto.Items, x => x.Id == 2);
    }
}
