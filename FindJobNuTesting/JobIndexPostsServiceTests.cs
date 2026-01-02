using FindjobnuService.DTOs.Requests;
using FindjobnuService.Models;
using FindjobnuService.Repositories.Context;
using FindjobnuService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace FindjobnuTesting
{
    public class JobIndexPostsServiceTests
    {
        private static FindjobnuContext GetDbContextWithData()
        {
            var options = new DbContextOptionsBuilder<FindjobnuContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var context = new FindjobnuContext(options);

            var itCategory = new Category { Name = "IT" };
            var designCategory = new Category { Name = "Design" };
            context.Categories.AddRange(itCategory, designCategory);
            context.JobIndexPosts.AddRange(
                new JobIndexPosts { JobID = 1, JobTitle = "Developer", JobLocation = "NY", Categories = [itCategory], Published = DateTime.UtcNow.AddDays(-1) },
                new JobIndexPosts { JobID = 2, JobTitle = "Designer", JobLocation = "LA", Categories = [designCategory], Published = DateTime.UtcNow.AddDays(-2) }
            );
            context.SaveChanges();
            return context;
        }

        [Fact]
        public async Task GetAllAsync_ReturnsPagedList()
        {
            var context = GetDbContextWithData();
            var logger = new Mock<ILogger<JobIndexPostsService>>().Object;
            var service = new JobIndexPostsService(context, logger);

            var result = await service.GetAllAsync(1, 10);

            Assert.NotNull(result);
            Assert.Equal(2, result.TotalCount);
            Assert.Equal(2, result.Items.Count());
        }

        [Fact]
        public async Task SearchAsync_FiltersByLocationAndCategory()
        {
            var context = GetDbContextWithData();
            var logger = new Mock<ILogger<JobIndexPostsService>>().Object;
            var service = new JobIndexPostsService(context, logger);
            var itCategoryId = await context.Categories.FirstAsync(c => c.Name == "IT");

            var result = await service.SearchAsync(null, "NY", itCategoryId.CategoryID, null, null, 1, 20);

            Assert.NotNull(result);
            Assert.Single(result.Items);
            Assert.Equal("Developer", result.Items.First().JobTitle);
        }

        [Fact]
        public async Task GetByIdAsync_ReturnsCorrectJob()
        {
            var context = GetDbContextWithData();
            var logger = new Mock<ILogger<JobIndexPostsService>>().Object;
            var service = new JobIndexPostsService(context, logger);

            var job = await service.GetByIdAsync(1);

            Assert.NotNull(job);
            Assert.Equal("Developer", job.JobTitle);
        }

        [Fact]
        public async Task GetCategoriesAsync_ReturnsDistinctCategories()
        {
            var context = GetDbContextWithData();
            var logger = new Mock<ILogger<JobIndexPostsService>>().Object;
            var service = new JobIndexPostsService(context, logger);

            var response = await service.GetCategoriesAsync();

            Assert.True(response.Success);
            Assert.Null(response.ErrorMessage);
            Assert.Contains(response.Categories, c => c.Name == "IT");
            Assert.Contains(response.Categories, c => c.Name == "Design");
            Assert.Equal(2, response.Categories.Count);
        }

        [Fact]
        public async Task GetSavedJobsByUserId_ReturnsPagedList_WhenUserHasSavedJobs()
        {
            var options = new DbContextOptionsBuilder<FindjobnuContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            using var context = new FindjobnuContext(options);
            var logger = new Mock<ILogger<JobIndexPostsService>>().Object;
            // Add categories
            var itCategory = new Category { Name = "IT" };
            context.Categories.Add(itCategory);
            // Add jobs
            var job1 = new JobIndexPosts { JobID = 10, JobTitle = "Dev", Categories = [itCategory], JobLocation = "NY", Published = DateTime.UtcNow };
            var job2 = new JobIndexPosts { JobID = 20, JobTitle = "QA", Categories = [itCategory], JobLocation = "NY", Published = DateTime.UtcNow };
            context.JobIndexPosts.AddRange(job1, job2);
            // Add user with saved jobs
            var user = new Profile { Id = 1, UserId = "userX", BasicInfo = new BasicInfo { FirstName = "Test", LastName = "User" }, SavedJobPosts = ["10", "20"] };
            context.Profiles.Add(user);
            await context.SaveChangesAsync();

            var service = new JobIndexPostsService(context, logger);
            var pagedList = await service.GetSavedJobsByUserId("userX", 1);

            Assert.NotNull(pagedList);
            Assert.Equal(2, pagedList.TotalCount);
            Assert.Equal(2, pagedList.Items.Count());
        }

        [Fact]
        public async Task GetSavedJobsByUserId_ReturnsEmpty_WhenUserHasNoSavedJobs()
        {
            var options = new DbContextOptionsBuilder<FindjobnuContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            using var context = new FindjobnuContext(options);
            var logger = new Mock<ILogger<JobIndexPostsService>>().Object;
            var user = new Profile { Id = 2, UserId = "userY", BasicInfo = new BasicInfo { FirstName = "Test", LastName = "User" }, SavedJobPosts = [] };
            context.Profiles.Add(user);
            await context.SaveChangesAsync();

            var service = new JobIndexPostsService(context, logger);
            var pagedList = await service.GetSavedJobsByUserId("userY", 1);
            Assert.NotNull(pagedList);
            Assert.Equal(0, pagedList.TotalCount);
            Assert.Empty(pagedList.Items);
        }

        [Fact]
        public async Task GetSavedJobsByUserId_ReturnsEmpty_WhenUserNotFound()
        {
            var options = new DbContextOptionsBuilder<FindjobnuContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            using var context = new FindjobnuContext(options);
            var logger = new Mock<ILogger<JobIndexPostsService>>().Object;
            var service = new JobIndexPostsService(context, logger);
            var pagedList = await service.GetSavedJobsByUserId("no_such_user", 1);
            Assert.NotNull(pagedList);
            Assert.Equal(0, pagedList.TotalCount);
            Assert.Empty(pagedList.Items);
        }

        [Fact]
        public async Task SearchAsync_LocationTokenMatchesMultipleDistricts()
        {
            var options = new DbContextOptionsBuilder<FindjobnuContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            using var context = new FindjobnuContext(options);
            var category = new Category { Name = "IT" };
            context.Categories.Add(category);
            context.JobIndexPosts.AddRange(
                new JobIndexPosts { JobID = 100, JobTitle = "Backend Dev", JobLocation = "København K", Categories = [category], Published = DateTime.UtcNow },
                new JobIndexPosts { JobID = 200, JobTitle = "Frontend Dev", JobLocation = "København V", Categories = [category], Published = DateTime.UtcNow }
            );
            await context.SaveChangesAsync();

            var logger = new Mock<ILogger<JobIndexPostsService>>().Object;
            var service = new JobIndexPostsService(context, logger);

            var result = await service.SearchAsync(null, "København K", category.CategoryID, null, null, 1, 20);

            Assert.Equal(2, result.TotalCount);
            Assert.Contains(result.Items, j => j.JobLocation == "København V");
            Assert.Contains(result.Items, j => j.JobLocation == "København K");
        }

        [Fact]
        public async Task GetRecommendedJobsByUserAndProfile_AppliesFiltersAfterRecommendation()
        {
            var options = new DbContextOptionsBuilder<FindjobnuContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            using var context = new FindjobnuContext(options);
            var logger = new Mock<ILogger<JobIndexPostsService>>().Object;

            var itCategory = new Category { Name = "IT" };
            var designCategory = new Category { Name = "Design" };
            context.Categories.AddRange(itCategory, designCategory);
            context.JobIndexPosts.AddRange(
                new JobIndexPosts { JobID = 10, JobTitle = "C# Developer", JobLocation = "NY", Categories = [itCategory], Published = DateTime.UtcNow },
                new JobIndexPosts { JobID = 20, JobTitle = "Designer", JobLocation = "NY", Categories = [designCategory], Published = DateTime.UtcNow }
            );
            context.Profiles.Add(new Profile
            {
                Id = 1,
                UserId = "user1",
                BasicInfo = new BasicInfo { FirstName = "Test", LastName = "User", JobTitle = "Developer" },
                Keywords = new List<string> { "Developer", "Designer" }
            });
            await context.SaveChangesAsync();

            var service = new JobIndexPostsService(context, logger);
            var request = new RecommendedJobsRequest(null, "NY", itCategory.CategoryID, null, null, 1, 10);

            var result = await service.GetRecommendedJobsByUserAndProfile("user1", request);

            Assert.NotNull(result);
            Assert.Single(result.Items);
            Assert.Equal(10, result.Items.First().JobID);
        }

        [Fact]
        public async Task GetStatisticsAsync_ComputesCountsAndTopCategories()
        {
            var options = new DbContextOptionsBuilder<FindjobnuContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            using var context = new FindjobnuContext(options);
            var tech = new Category { Name = "Tech" };
            var health = new Category { Name = "Health" };
            var design = new Category { Name = "Design" };
            context.Categories.AddRange(tech, health, design);
            var now = DateTime.UtcNow;
            context.JobIndexPosts.AddRange(
                new JobIndexPosts { JobID = 1, JobTitle = "A", Categories = [tech], Published = now.AddDays(-2) },
                new JobIndexPosts { JobID = 2, JobTitle = "B", Categories = [tech], Published = now.AddDays(-10) },
                new JobIndexPosts { JobID = 3, JobTitle = "C", Categories = [health], Published = now.AddDays(-3) },
                new JobIndexPosts { JobID = 4, JobTitle = "D", Categories = [health], Published = now.AddDays(-40) },
                new JobIndexPosts { JobID = 5, JobTitle = "E", Categories = [design], Published = now.AddDays(-1) },
                new JobIndexPosts { JobID = 6, JobTitle = "F", Categories = [design], Published = now.AddDays(-20) },
                new JobIndexPosts { JobID = 7, JobTitle = "G", Categories = [health], Published = now.AddDays(-2) }
            );
            await context.SaveChangesAsync();

            var logger = new Mock<ILogger<JobIndexPostsService>>().Object;
            var service = new JobIndexPostsService(context, logger);

            var stats = await service.GetStatisticsAsync();

            Assert.Equal(7, stats.TotalJobs);
            Assert.Equal(4, stats.NewJobsLastWeek);
            Assert.Equal(6, stats.NewJobsLastMonth);
            Assert.Equal("Health", stats.TopCategories.First().Name);
            Assert.Equal(3, stats.TopCategories.First().NumberOfJobs);
            Assert.Equal("Health", stats.TopCategoriesLastWeek.First().Name);
            Assert.Equal(2, stats.TopCategoriesLastWeek.First().NumberOfJobs);
        }
    }
}