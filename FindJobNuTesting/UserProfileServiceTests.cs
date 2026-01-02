using FindjobnuService.Models;
using FindjobnuService.Repositories.Context;
using FindjobnuService.Services;
using Microsoft.EntityFrameworkCore;

namespace FindjobnuTesting
{
    public class ProfileServiceTests_User
    {
        private ProfileService GetServiceWithInMemoryDb(out FindjobnuContext context)
        {
            var options = new DbContextOptionsBuilder<FindjobnuContext>()
                .UseInMemoryDatabase(databaseName: "TestDb")
                .Options;
            context = new FindjobnuContext(options);
            return new ProfileService(context);
        }

        [Fact]
        public async Task GetByIdAsync_ReturnsProfile_WhenExists()
        {
            var service = GetServiceWithInMemoryDb(out var context);
            var profile = new Profile
            {
                UserId = "user1",
                Keywords = new List<string> { "developer", "csharp" },
                BasicInfo = new BasicInfo { FirstName = "John", LastName = "Doe" }
            };
            context.Profiles.Add(profile);
            await context.SaveChangesAsync();

            var result = await service.GetByUserIdAsync("user1");

            Assert.NotNull(result);
            Assert.Equal("John", result!.BasicInfo.FirstName);
            Assert.Contains("developer", result.Keywords);
        }

        [Fact]
        public async Task CreateAsync_AddsProfile()
        {
            var service = GetServiceWithInMemoryDb(out var context);
            var profile = new Profile
            {
                UserId = "user2",
                Keywords = new List<string> { "qa" },
                BasicInfo = new BasicInfo { FirstName = "Jane", LastName = "Smith" }
            };

            var result = await service.CreateAsync(profile);

            Assert.NotNull(result);
            Assert.Equal("Jane", result!.BasicInfo.FirstName);
            Assert.Contains("qa", result.Keywords);
        }

        [Fact]
        public async Task UpdateAsync_UpdatesProfile_WhenUserIdMatches()
        {
            var service = GetServiceWithInMemoryDb(out var context);
            var profile = new Profile
            {
                Id = 2,
                UserId = "user3",
                Keywords = new List<string> { "old" },
                BasicInfo = new BasicInfo { FirstName = "Alice", LastName = "Brown" }
            };
            context.Profiles.Add(profile);
            await context.SaveChangesAsync();

            var updatedProfile = new Profile
            {
                UserId = "user3",
                Keywords = new List<string> { "new" },
                BasicInfo = new BasicInfo { FirstName = "Alicia", LastName = "Brown" }
            };
            var result = await service.UpdateAsync(2, updatedProfile, "user3");

            Assert.True(result);
            var dbProfile = await context.Profiles.FindAsync(2);
            Assert.Equal("Alicia", dbProfile!.BasicInfo.FirstName);
            Assert.Contains("new", dbProfile.Keywords);
        }

        [Fact]
        public async Task GetSavedJobsByUserIdAsync_ReturnsSavedJobs()
        {
            var service = GetServiceWithInMemoryDb(out var context);
            var profile = new Profile
            {
                Id = 3,
                UserId = "user4",
                SavedJobPosts = new List<string> { "1", "2" },
                Keywords = new List<string> { "test" },
                BasicInfo = new BasicInfo { FirstName = "Bob", LastName = "Smith" }
            };
            var job1 = new JobIndexPosts
            {
                JobID = 1,
                JobDescription = "Job1"
            };
            var job2 = new JobIndexPosts
            {
                JobID = 2,
                JobDescription = "Job2"
            };
            context.JobIndexPosts.AddRange(job1, job2);
            context.Profiles.Add(profile);
            await context.SaveChangesAsync();

            var result = await service.GetSavedJobsByUserIdAsync("user4");

            Assert.NotNull(result);
            Assert.Equal(2, result.Items.Count());
            Assert.Contains(result.Items, j => j.JobID.ToString() == "1");
            Assert.Contains(result.Items, j => j.JobID.ToString() == "2");
        }

        [Fact]
        public async Task GetSavedJobsByUserIdAsync_ReturnsEmptyList_WhenUserNotFound()
        {
            var service = GetServiceWithInMemoryDb(out var context);
            var result = await service.GetSavedJobsByUserIdAsync("no_user");
            Assert.NotNull(result);
            Assert.Empty(result.Items);
        }

        [Fact]
        public async Task SaveJobAsync_AddsJobToSavedList()
        {
            var service = GetServiceWithInMemoryDb(out var context);
            var profile = new Profile
            {
                Id = 4,
                UserId = "user5",
                SavedJobPosts = new List<string>(),
                Keywords = new List<string> { "save" },
                BasicInfo = new BasicInfo { FirstName = "Eve", LastName = "Adams" }
            };
            context.Profiles.Add(profile);
            await context.SaveChangesAsync();

            var result = await service.SaveJobAsync("user5", "99");
            Assert.True(result);
            var dbProfile = await context.Profiles.FirstOrDefaultAsync(u => u.UserId == "user5");
            Assert.Contains("99", dbProfile!.SavedJobPosts);
        }

        [Fact]
        public async Task SaveJobAsync_DoesNotAddDuplicateJob()
        {
            var service = GetServiceWithInMemoryDb(out var context);
            var profile = new Profile
            {
                Id = 5,
                UserId = "user6",
                SavedJobPosts = new List<string> { "100" },
                Keywords = new List<string> { "dup" },
                BasicInfo = new BasicInfo { FirstName = "Sam", LastName = "Lee" }
            };
            context.Profiles.Add(profile);
            await context.SaveChangesAsync();

            var result = await service.SaveJobAsync("user6", "100");
            Assert.False(result);
        }

        [Fact]
        public async Task SaveJobAsync_ReturnsFalse_WhenUserNotFound()
        {
            var service = GetServiceWithInMemoryDb(out var context);
            var result = await service.SaveJobAsync("no_user", "1");
            Assert.False(result);
        }

        [Fact]
        public async Task RemoveSavedJobAsync_RemovesJobFromSavedList()
        {
            var service = GetServiceWithInMemoryDb(out var context);
            var profile = new Profile
            {
                Id = 6,
                UserId = "user7",
                SavedJobPosts = new List<string> { "200" },
                Keywords = new List<string> { "remove" },
                BasicInfo = new BasicInfo { FirstName = "Tom", LastName = "Jones" }
            };
            context.Profiles.Add(profile);
            await context.SaveChangesAsync();

            var result = await service.RemoveSavedJobAsync("user7", "200");
            Assert.True(result);
            var dbProfile = await context.Profiles.FirstOrDefaultAsync(u => u.UserId == "user7");
            Assert.DoesNotContain("200", dbProfile!.SavedJobPosts);
        }

        [Fact]
        public async Task RemoveSavedJobAsync_ReturnsFalse_WhenJobNotInList()
        {
            var service = GetServiceWithInMemoryDb(out var context);
            var profile = new Profile
            {
                Id = 8,
                UserId = "user8",
                SavedJobPosts = new List<string> { "300" },
                Keywords = new List<string> { "notfound" },
                BasicInfo = new BasicInfo { FirstName = "Ann", LastName = "White" }
            };
            context.Profiles.Add(profile);
            await context.SaveChangesAsync();

            var result = await service.RemoveSavedJobAsync("user8", "999");
            Assert.False(result);
        }

        [Fact]
        public async Task RemoveSavedJobAsync_ReturnsFalse_WhenUserNotFound()
        {
            var service = GetServiceWithInMemoryDb(out var context);
            var result = await service.RemoveSavedJobAsync("no_user", "1");
            Assert.False(result);
        }
    }
}
