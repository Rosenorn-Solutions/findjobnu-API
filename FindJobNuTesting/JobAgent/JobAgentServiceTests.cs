using FindjobnuService.Models;
using FindjobnuService.Repositories.Context;
using FindjobnuService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace FindjobnuTesting.JobAgentTests
{
    public class JobAgentServiceTests
    {
        private FindjobnuContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<FindjobnuContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            return new FindjobnuContext(options);
        }

        private IJobAgentService CreateService(FindjobnuContext ctx)
        {
            var logger = new Mock<ILogger<JobAgentService>>().Object;
            return new JobAgentService(ctx, logger);
        }

        [Fact]
        public async Task CreateOrUpdateAsync_CreatesAgent_WhenNoneExists()
        {
            var ctx = CreateContext();
            var profile = new Profile { UserId = "u1", BasicInfo = new BasicInfo { FirstName = "A", LastName = "B" } };
            ctx.Profiles.Add(profile);
            ctx.SaveChanges();
            var svc = CreateService(ctx);

            var agent = await svc.CreateOrUpdateAsync(profile.Id, true, JobAgentFrequency.Weekly, null, null, null);
            Assert.NotNull(agent);
            Assert.True(agent.Enabled);
            Assert.Equal(JobAgentFrequency.Weekly, agent.Frequency);
        }

        [Fact]
        public async Task CreateOrUpdateAsync_UpdatesExistingAgent()
        {
            var ctx = CreateContext();
            var profile = new Profile { UserId = "u2", BasicInfo = new BasicInfo { FirstName = "A", LastName = "B" } };
            ctx.Profiles.Add(profile);
            ctx.SaveChanges();
            var svc = CreateService(ctx);
            var first = await svc.CreateOrUpdateAsync(profile.Id, false, JobAgentFrequency.Weekly, null, null, null);

            var updated = await svc.CreateOrUpdateAsync(profile.Id, true, JobAgentFrequency.Daily, null, null, null);
            Assert.True(updated.Enabled);
            Assert.Equal(JobAgentFrequency.Daily, updated.Frequency);
            Assert.Equal(first.Id, updated.Id);
        }

        [Fact]
        public async Task GetOrCreateUnsubscribeTokenAsync_ReturnsToken()
        {
            var ctx = CreateContext();
            var profile = new Profile { UserId = "u3", BasicInfo = new BasicInfo { FirstName = "A", LastName = "B" } };
            ctx.Profiles.Add(profile);
            ctx.SaveChanges();
            var svc = CreateService(ctx);
            await svc.CreateOrUpdateAsync(profile.Id, true, JobAgentFrequency.Weekly, null, null, null);

            var token = await svc.GetOrCreateUnsubscribeTokenAsync(profile.Id);
            Assert.False(string.IsNullOrWhiteSpace(token));
        }

        [Fact]
        public async Task UnsubscribeByTokenAsync_DisablesAgent_WhenValidToken()
        {
            var ctx = CreateContext();
            var profile = new Profile { UserId = "u4", BasicInfo = new BasicInfo { FirstName = "A", LastName = "B" } };
            ctx.Profiles.Add(profile);
            ctx.SaveChanges();
            var svc = CreateService(ctx);
            var agent = await svc.CreateOrUpdateAsync(profile.Id, true, JobAgentFrequency.Weekly, null, null, null);
            var token = await svc.GetOrCreateUnsubscribeTokenAsync(profile.Id);
            Assert.NotNull(token);

            var ok = await svc.UnsubscribeByTokenAsync(token!);
            Assert.True(ok);
            var reloaded = await ctx.JobAgents.FirstAsync(a => a.Id == agent.Id);
            Assert.False(reloaded.Enabled);
        }

        [Fact]
        public async Task UnsubscribeByTokenAsync_ReturnsFalse_WhenInvalidToken()
        {
            var ctx = CreateContext();
            var svc = CreateService(ctx);
            var ok = await svc.UnsubscribeByTokenAsync("bogus");
            Assert.False(ok);
        }

        [Fact]
        public async Task GetCategoriesByIdsAsync_ReturnsMatchingCategories()
        {
            var ctx = CreateContext();
            var categories = new List<Category>
            {
                new Category { CategoryID = 1, Name = "IT" },
                new Category { CategoryID = 2, Name = "Finance" },
                new Category { CategoryID = 3, Name = "Marketing" }
            };
            ctx.Categories.AddRange(categories);
            await ctx.SaveChangesAsync();
            var svc = CreateService(ctx);

            var result = await svc.GetCategoriesByIdsAsync(new[] { 1, 3 });

            Assert.NotNull(result);
            Assert.Equal(2, result.Count());
            Assert.Contains(result, c => c.Name == "IT");
            Assert.Contains(result, c => c.Name == "Marketing");
            Assert.DoesNotContain(result, c => c.Name == "Finance");
        }

        [Fact]
        public async Task GetCategoriesByIdsAsync_ReturnsEmpty_WhenNoMatches()
        {
            var ctx = CreateContext();
            var categories = new List<Category>
            {
                new Category { CategoryID = 1, Name = "IT" }
            };
            ctx.Categories.AddRange(categories);
            await ctx.SaveChangesAsync();
            var svc = CreateService(ctx);

            var result = await svc.GetCategoriesByIdsAsync(new[] { 99, 100 });

            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public async Task GetCategoriesByIdsAsync_ReturnsEmpty_WhenIdsIsNull()
        {
            var ctx = CreateContext();
            var svc = CreateService(ctx);

            var result = await svc.GetCategoriesByIdsAsync(null!);

            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public async Task GetCategoriesByIdsAsync_ReturnsEmpty_WhenIdsIsEmpty()
        {
            var ctx = CreateContext();
            var svc = CreateService(ctx);

            var result = await svc.GetCategoriesByIdsAsync(new List<int>());

            Assert.NotNull(result);
            Assert.Empty(result);
        }
    }
}
