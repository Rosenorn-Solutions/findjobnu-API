using FindjobnuService.Mappers;
using FindjobnuService.Models;

namespace FindjobnuTesting.JobAgentTests
{
    public class JobAgentMapperTests
    {
        [Fact]
        public void ToDto_ReturnsNull_WhenAgentIsNull()
        {
            var result = JobAgentMapper.ToDto(null);
            Assert.Null(result);
        }

        [Fact]
        public void ToDto_MapsAllFields_WithoutCategories()
        {
            var agent = new JobAgent
            {
                Id = 1,
                ProfileId = 100,
                Enabled = true,
                Frequency = JobAgentFrequency.Weekly,
                LastSentAt = DateTime.UtcNow.AddDays(-7),
                NextSendAt = DateTime.UtcNow.AddDays(7),
                CreatedAt = DateTime.UtcNow.AddMonths(-1),
                UpdatedAt = DateTime.UtcNow,
                PreferredLocations = new List<string> { "Copenhagen", "Aarhus" },
                PreferredCategoryIds = new List<int> { 1, 2, 3 },
                IncludeKeywords = new List<string> { "C#", ".NET" }
            };

            var result = JobAgentMapper.ToDto(agent);

            Assert.NotNull(result);
            Assert.Equal(1, result.Id);
            Assert.Equal(100, result.ProfileId);
            Assert.True(result.Enabled);
            Assert.Equal(JobAgentFrequency.Weekly, result.Frequency);
            Assert.Equal(agent.LastSentAt, result.LastSentAt);
            Assert.Equal(agent.NextSendAt, result.NextSendAt);
            Assert.Equal(agent.CreatedAt, result.CreatedAt);
            Assert.Equal(agent.UpdatedAt, result.UpdatedAt);
            Assert.Equal(2, result.PreferredLocations.Count);
            Assert.Contains("Copenhagen", result.PreferredLocations);
            Assert.Equal(3, result.PreferredCategoryIds.Count);
            Assert.Contains(1, result.PreferredCategoryIds);
            Assert.Empty(result.PreferredCategoryNames);
            Assert.Equal(2, result.IncludeKeywords.Count);
            Assert.Contains("C#", result.IncludeKeywords);
        }

        [Fact]
        public void ToDto_MapsAllFields_WithCategories()
        {
            var agent = new JobAgent
            {
                Id = 2,
                ProfileId = 200,
                Enabled = false,
                Frequency = JobAgentFrequency.Daily,
                PreferredCategoryIds = new List<int> { 1, 2, 3 }
            };

            var categories = new List<Category>
            {
                new Category { CategoryID = 1, Name = "IT" },
                new Category { CategoryID = 2, Name = "Finance" },
                new Category { CategoryID = 3, Name = "Marketing" }
            };

            var result = JobAgentMapper.ToDto(agent, categories);

            Assert.NotNull(result);
            Assert.Equal(3, result.PreferredCategoryIds.Count);
            Assert.Equal(3, result.PreferredCategoryNames.Count);
            Assert.Contains("IT", result.PreferredCategoryNames);
            Assert.Contains("Finance", result.PreferredCategoryNames);
            Assert.Contains("Marketing", result.PreferredCategoryNames);
        }

        [Fact]
        public void ToDto_HandlesPartialCategoryMatches()
        {
            var agent = new JobAgent
            {
                Id = 3,
                ProfileId = 300,
                Enabled = true,
                Frequency = JobAgentFrequency.Monthly,
                PreferredCategoryIds = new List<int> { 1, 2, 99 } // 99 doesn't exist
            };

            var categories = new List<Category>
            {
                new Category { CategoryID = 1, Name = "IT" },
                new Category { CategoryID = 2, Name = "Finance" }
            };

            var result = JobAgentMapper.ToDto(agent, categories);

            Assert.NotNull(result);
            Assert.Equal(3, result.PreferredCategoryIds.Count);
            Assert.Equal(2, result.PreferredCategoryNames.Count); // Only matching categories
            Assert.Contains("IT", result.PreferredCategoryNames);
            Assert.Contains("Finance", result.PreferredCategoryNames);
            Assert.DoesNotContain("Unknown", result.PreferredCategoryNames);
        }

        [Fact]
        public void ToDto_HandlesNullPreferredCategoryIds()
        {
            var agent = new JobAgent
            {
                Id = 4,
                ProfileId = 400,
                Enabled = true,
                Frequency = JobAgentFrequency.Weekly,
                PreferredCategoryIds = null
            };

            var categories = new List<Category>
            {
                new Category { CategoryID = 1, Name = "IT" }
            };

            var result = JobAgentMapper.ToDto(agent, categories);

            Assert.NotNull(result);
            Assert.Empty(result.PreferredCategoryIds);
            Assert.Empty(result.PreferredCategoryNames);
        }

        [Fact]
        public void ToDto_HandlesEmptyPreferredCategoryIds()
        {
            var agent = new JobAgent
            {
                Id = 5,
                ProfileId = 500,
                Enabled = true,
                Frequency = JobAgentFrequency.Weekly,
                PreferredCategoryIds = new List<int>()
            };

            var categories = new List<Category>
            {
                new Category { CategoryID = 1, Name = "IT" }
            };

            var result = JobAgentMapper.ToDto(agent, categories);

            Assert.NotNull(result);
            Assert.Empty(result.PreferredCategoryIds);
            Assert.Empty(result.PreferredCategoryNames);
        }

        [Fact]
        public void ToDto_HandlesNullCategories()
        {
            var agent = new JobAgent
            {
                Id = 6,
                ProfileId = 600,
                Enabled = true,
                Frequency = JobAgentFrequency.Weekly,
                PreferredCategoryIds = new List<int> { 1, 2 }
            };

            var result = JobAgentMapper.ToDto(agent, null);

            Assert.NotNull(result);
            Assert.Equal(2, result.PreferredCategoryIds.Count);
            Assert.Empty(result.PreferredCategoryNames);
        }
    }
}
