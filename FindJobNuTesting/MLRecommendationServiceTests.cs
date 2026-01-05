using FindjobnuService.Models;
using FindjobnuService.Repositories.Context;
using FindjobnuService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;

namespace FindjobnuTesting
{
    public class MLRecommendationServiceTests
    {
        private static FindjobnuContext GetInMemoryDbContext()
        {
            var options = new DbContextOptionsBuilder<FindjobnuContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            return new FindjobnuContext(options);
        }

        [Fact]
        public async Task GetRecommendationsAsync_ReturnsEmptyList_WhenNoJobs()
        {
            using var context = GetInMemoryDbContext();
            var logger = new Mock<ILogger<MLRecommendationService>>().Object;
            var cache = new MemoryCache(new MemoryCacheOptions());
            var service = new MLRecommendationService(context, logger, cache);

            var profile = new Profile
            {
                UserId = "user1",
                BasicInfo = new BasicInfo { FirstName = "Test", LastName = "User", JobTitle = "Developer" }
            };

            var recommendations = await service.GetRecommendationsAsync("user1", profile, 10);

            Assert.NotNull(recommendations);
            Assert.Empty(recommendations);
        }

        [Fact]
        public async Task GetRecommendationsAsync_ReturnsRecommendations_WhenJobsExist()
        {
            using var context = GetInMemoryDbContext();
            var logger = new Mock<ILogger<MLRecommendationService>>().Object;
            var cache = new MemoryCache(new MemoryCacheOptions());

            var category = new Category { Name = "Software Development" };
            context.Categories.Add(category);
            
            context.JobIndexPosts.AddRange(
                new JobIndexPosts { JobID = 1, JobTitle = "Senior C# Developer", CompanyName = "TechCorp", JobLocation = "Copenhagen", Published = DateTime.UtcNow.AddDays(-1), Categories = new List<Category> { category } },
                new JobIndexPosts { JobID = 2, JobTitle = "Java Engineer", CompanyName = "JavaCo", JobLocation = "Aarhus", Published = DateTime.UtcNow.AddDays(-2), Categories = new List<Category> { category } },
                new JobIndexPosts { JobID = 3, JobTitle = "Python Developer", CompanyName = "PyTech", JobLocation = "Odense", Published = DateTime.UtcNow.AddDays(-3), Categories = new List<Category> { category } }
            );
            await context.SaveChangesAsync();

            var profile = new Profile
            {
                UserId = "user1",
                BasicInfo = new BasicInfo { FirstName = "Test", LastName = "User", JobTitle = "C# Developer", Location = "Copenhagen" },
                Keywords = new List<string> { "C#", "Developer" },
                Skills = new List<Skill> { new Skill { Name = "C#", Proficiency = SkillProficiency.Expert } }
            };

            var service = new MLRecommendationService(context, logger, cache);
            var recommendations = await service.GetRecommendationsAsync("user1", profile, 10);

            Assert.NotNull(recommendations);
            Assert.NotEmpty(recommendations);
            Assert.True(recommendations.Count <= 3);
            
            // C# Developer job should score highest
            var topRecommendation = recommendations.First();
            Assert.Equal(1, topRecommendation.JobId);
            Assert.True(topRecommendation.Score > 0);
        }

        [Fact]
        public async Task RecordInteractionAsync_AddsInteraction_ToDatabase()
        {
            using var context = GetInMemoryDbContext();
            var logger = new Mock<ILogger<MLRecommendationService>>().Object;
            var cache = new MemoryCache(new MemoryCacheOptions());
            var service = new MLRecommendationService(context, logger, cache);

            context.JobIndexPosts.Add(new JobIndexPosts { JobID = 1, JobTitle = "Test Job", Published = DateTime.UtcNow });
            await context.SaveChangesAsync();

            await service.RecordInteractionAsync("user1", 1, InteractionType.View, 45);

            var interaction = await context.UserJobInteractions.FirstOrDefaultAsync();
            Assert.NotNull(interaction);
            Assert.Equal("user1", interaction.UserId);
            Assert.Equal(1, interaction.JobId);
            Assert.Equal(InteractionType.View, interaction.InteractionType);
            Assert.Equal(45, interaction.DurationSeconds);
            Assert.Equal(1, interaction.Score);
        }

        [Fact]
        public async Task GetRecommendationsAsync_ConsidersImplicitFeedback_WhenUserHasInteractions()
        {
            using var context = GetInMemoryDbContext();
            var logger = new Mock<ILogger<MLRecommendationService>>().Object;
            var cache = new MemoryCache(new MemoryCacheOptions());

            var category = new Category { Name = "Development" };
            context.Categories.Add(category);
            
            context.JobIndexPosts.AddRange(
                new JobIndexPosts { JobID = 1, JobTitle = "Developer", CompanyName = "CompanyA", Published = DateTime.UtcNow.AddDays(-1), Categories = new List<Category> { category } },
                new JobIndexPosts { JobID = 2, JobTitle = "Engineer", CompanyName = "CompanyB", Published = DateTime.UtcNow.AddDays(-2), Categories = new List<Category> { category } }
            );

            context.UserJobInteractions.AddRange(
                new UserJobInteraction { UserId = "user1", JobId = 1, InteractionType = InteractionType.Save, Score = 3, InteractionDate = DateTime.UtcNow },
                new UserJobInteraction { UserId = "user1", JobId = 2, InteractionType = InteractionType.View, Score = 1, InteractionDate = DateTime.UtcNow }
            );

            await context.SaveChangesAsync();

            var profile = new Profile
            {
                UserId = "user1",
                BasicInfo = new BasicInfo { FirstName = "Test", LastName = "User", JobTitle = "Developer" },
                Keywords = new List<string> { "Developer" }
            };

            var service = new MLRecommendationService(context, logger, cache);
            var recommendations = await service.GetRecommendationsAsync("user1", profile, 10);

            Assert.NotNull(recommendations);
            Assert.NotEmpty(recommendations);
            
            // Job 1 should rank higher due to Save interaction (higher score)
            var job1Score = recommendations.First(r => r.JobId == 1).Score;
            var job2Score = recommendations.First(r => r.JobId == 2).Score;
            Assert.True(job1Score > job2Score);
        }

        [Fact]
        public async Task GetRecommendationsAsync_PenalizesAppliedJobs()
        {
            using var context = GetInMemoryDbContext();
            var logger = new Mock<ILogger<MLRecommendationService>>().Object;
            var cache = new MemoryCache(new MemoryCacheOptions());

            var category = new Category { Name = "IT" };
            context.Categories.Add(category);
            
            context.JobIndexPosts.AddRange(
                new JobIndexPosts { JobID = 1, JobTitle = "Developer", Published = DateTime.UtcNow, Categories = new List<Category> { category } },
                new JobIndexPosts { JobID = 2, JobTitle = "Developer", Published = DateTime.UtcNow, Categories = new List<Category> { category } }
            );

            context.UserJobInteractions.Add(
                new UserJobInteraction { UserId = "user1", JobId = 1, InteractionType = InteractionType.Apply, Score = 5, InteractionDate = DateTime.UtcNow }
            );

            await context.SaveChangesAsync();

            var profile = new Profile
            {
                UserId = "user1",
                BasicInfo = new BasicInfo { FirstName = "Test", LastName = "User", JobTitle = "Developer" },
                Keywords = new List<string> { "Developer" }
            };

            var service = new MLRecommendationService(context, logger, cache);
            var recommendations = await service.GetRecommendationsAsync("user1", profile, 10);

            // Job 2 should score higher than Job 1 (applied job should be penalized)
            var job1Score = recommendations.First(r => r.JobId == 1).Score;
            var job2Score = recommendations.First(r => r.JobId == 2).Score;
            Assert.True(job2Score > job1Score);
        }

        [Fact]
        public async Task GetRecommendationsAsync_UsesCaching()
        {
            using var context = GetInMemoryDbContext();
            var logger = new Mock<ILogger<MLRecommendationService>>().Object;
            var cache = new MemoryCache(new MemoryCacheOptions());

            var category = new Category { Name = "IT" };
            context.Categories.Add(category);
            context.JobIndexPosts.Add(new JobIndexPosts { JobID = 1, JobTitle = "Developer", Published = DateTime.UtcNow, Categories = new List<Category> { category } });
            await context.SaveChangesAsync();

            var profile = new Profile
            {
                UserId = "user1",
                BasicInfo = new BasicInfo { FirstName = "Test", LastName = "User", JobTitle = "Developer" },
                Keywords = new List<string> { "Developer" }
            };

            var service = new MLRecommendationService(context, logger, cache);
            
            var recommendations1 = await service.GetRecommendationsAsync("user1", profile, 10);
            var recommendations2 = await service.GetRecommendationsAsync("user1", profile, 10);

            Assert.Equal(recommendations1.Count, recommendations2.Count);
            Assert.Equal(recommendations1.First().JobId, recommendations2.First().JobId);
        }

        [Fact]
        public async Task RecordInteractionAsync_AssignsCorrectScores()
        {
            using var context = GetInMemoryDbContext();
            var logger = new Mock<ILogger<MLRecommendationService>>().Object;
            var cache = new MemoryCache(new MemoryCacheOptions());
            var service = new MLRecommendationService(context, logger, cache);

            context.JobIndexPosts.Add(new JobIndexPosts { JobID = 1, JobTitle = "Test", Published = DateTime.UtcNow });
            await context.SaveChangesAsync();

            await service.RecordInteractionAsync("user1", 1, InteractionType.View);
            await service.RecordInteractionAsync("user1", 1, InteractionType.Click);
            await service.RecordInteractionAsync("user1", 1, InteractionType.Save);
            await service.RecordInteractionAsync("user1", 1, InteractionType.Apply);

            var interactions = await context.UserJobInteractions.ToListAsync();
            
            Assert.Equal(1, interactions.First(i => i.InteractionType == InteractionType.View).Score);
            Assert.Equal(2, interactions.First(i => i.InteractionType == InteractionType.Click).Score);
            Assert.Equal(3, interactions.First(i => i.InteractionType == InteractionType.Save).Score);
            Assert.Equal(5, interactions.First(i => i.InteractionType == InteractionType.Apply).Score);
        }
    }
}
