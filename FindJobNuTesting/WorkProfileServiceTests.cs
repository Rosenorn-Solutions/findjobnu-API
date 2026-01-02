using FindjobnuService.Models;
using FindjobnuService.Repositories.Context;
using FindjobnuService.Services;
using Microsoft.EntityFrameworkCore;

namespace FindjobnuTesting
{
    public class ProfileServiceTests
    {
        private ProfileService GetServiceWithInMemoryDb(out FindjobnuContext context)
        {
            var options = new DbContextOptionsBuilder<FindjobnuContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            context = new FindjobnuContext(options);
            return new ProfileService(context);
        }

        [Fact]
        public async Task CreateAsync_AddsProfile()
        {
            var service = GetServiceWithInMemoryDb(out var context);
            var profile = new Profile { UserId = "user1", BasicInfo = new BasicInfo { FirstName = "Test", LastName = "User" } };

            var result = await service.CreateAsync(profile);

            Assert.NotNull(result);
            Assert.Equal("user1", result!.UserId);
            Assert.Equal("Test", result.BasicInfo!.FirstName);
        }

        [Fact]
        public async Task GetByUserIdAsync_ReturnsProfileWithRelations()
        {
            var service = GetServiceWithInMemoryDb(out var context);
            var profile = new Profile
            {
                UserId = "user2",
                BasicInfo = new BasicInfo { FirstName = "Test", LastName = "User" },
                Experiences = new List<Experience> { new Experience { PositionTitle = "Dev" } },
                Educations = new List<Education> { new Education { Institution = "Uni" } },
                Interests = new List<Interest> { new Interest { Title = "Coding" } },
                Accomplishments = new List<Accomplishment> { new Accomplishment { Title = "Award" } },
                Contacts = new List<Contact> { new Contact { Name = "Contact1" } }
            };
            context.Profiles.Add(profile);
            context.SaveChanges();

            var result = await service.GetByUserIdAsync("user2");

            Assert.NotNull(result);
            Assert.Equal("Test", result!.BasicInfo!.FirstName);
            Assert.Single(result.Experiences!);
            Assert.Single(result.Educations!);
            Assert.Single(result.Interests!);
            Assert.Single(result.Accomplishments!);
            Assert.Single(result.Contacts!);
        }

        [Fact]
        public async Task UpdateAsync_UpdatesAllRelations()
        {
            var service = GetServiceWithInMemoryDb(out var context);
            var profile = new Profile
            {
                UserId = "user3",
                BasicInfo = new BasicInfo { FirstName = "Test", LastName = "User" },
                Experiences = new List<Experience> { new Experience { PositionTitle = "OldExp" } },
                Educations = new List<Education> { new Education { Institution = "OldUni" } },
                Interests = new List<Interest> { new Interest { Title = "OldInterest" } },
                Accomplishments = new List<Accomplishment> { new Accomplishment { Title = "OldAcc" } },
                Contacts = new List<Contact> { new Contact { Name = "OldContact" } }
            };
            context.Profiles.Add(profile);
            context.SaveChanges();

            var updated = new Profile
            {
                UserId = "user3",
                BasicInfo = new BasicInfo { FirstName = "Test", LastName = "User" },
                Experiences = new List<Experience> { new Experience { PositionTitle = "NewExp" } },
                Educations = new List<Education> { new Education { Institution = "NewUni" } },
                Interests = new List<Interest> { new Interest { Title = "NewInterest" } },
                Accomplishments = new List<Accomplishment> { new Accomplishment { Title = "NewAcc" } },
                Contacts = new List<Contact> { new Contact { Name = "NewContact" } }
            };

            var result = await service.UpdateAsync(profile.Id, updated, "user3");
            Assert.True(result);

            var dbProfile = await context.Profiles
                .Include(p => p.Experiences)
                .Include(p => p.Educations)
                .Include(p => p.Interests)
                .Include(p => p.Accomplishments)
                .Include(p => p.Contacts)
                .FirstOrDefaultAsync(p => p.Id == profile.Id);

            Assert.NotNull(dbProfile);
            Assert.Single(dbProfile.Experiences!);
            Assert.Equal("NewExp", dbProfile.Experiences!.First().PositionTitle);
            Assert.Single(dbProfile.Educations!);
            Assert.Equal("NewUni", dbProfile.Educations!.First().Institution);
            Assert.Single(dbProfile.Interests!);
            Assert.Equal("NewInterest", dbProfile.Interests!.First().Title);
            Assert.Single(dbProfile.Accomplishments!);
            Assert.Equal("NewAcc", dbProfile.Accomplishments!.First().Title);
            Assert.Single(dbProfile.Contacts!);
            Assert.Equal("NewContact", dbProfile.Contacts!.First().Name);
        }

        [Fact]
        public async Task UpdateAsync_ReturnsFalse_WhenNotFoundOrUnauthorized()
        {
            var service = GetServiceWithInMemoryDb(out var context);
            var profile = new Profile { UserId = "user5", BasicInfo = new BasicInfo { FirstName = "Test", LastName = "User" } };
            context.Profiles.Add(profile);
            context.SaveChanges();

            var updated = new Profile { UserId = "user5", BasicInfo = new BasicInfo { FirstName = "Test", LastName = "User" } };
            var result = await service.UpdateAsync(999, updated, "user5");
            Assert.False(result);
            result = await service.UpdateAsync(profile.Id, updated, "wronguser");
            Assert.False(result);
        }

        [Fact]
        public async Task CreateAsync_AddsProfileWithSkills()
        {
            var service = GetServiceWithInMemoryDb(out var context);
            var profile = new Profile
            {
                UserId = "user1",
                BasicInfo = new BasicInfo { FirstName = "Test", LastName = "User" },
                Skills = new List<Skill> { new Skill { Name = "C#", Proficiency = SkillProficiency.Expert } }
            };

            var result = await service.CreateAsync(profile);

            Assert.NotNull(result);
            Assert.Single(result!.Skills!);
            Assert.Equal("C#", result.Skills!.First().Name);
            Assert.Equal(SkillProficiency.Expert, result.Skills!.First().Proficiency);
        }

        [Fact]
        public async Task UpdateAsync_UpdatesSkills()
        {
            var service = GetServiceWithInMemoryDb(out var context);
            var profile = new Profile
            {
                UserId = "user6",
                BasicInfo = new BasicInfo { FirstName = "Test", LastName = "User" },
                Skills = new List<Skill> { new Skill { Name = "Java", Proficiency = SkillProficiency.Beginner } }
            };
            context.Profiles.Add(profile);
            context.SaveChanges();

            var updated = new Profile
            {
                UserId = "user6",
                BasicInfo = new BasicInfo { FirstName = "Test", LastName = "User" },
                Skills = new List<Skill> { new Skill { Name = "Python", Proficiency = SkillProficiency.Advanced } }
            };

            var result = await service.UpdateAsync(profile.Id, updated, "user6");
            Assert.True(result);

            var dbProfile = await context.Profiles
                .Include(p => p.Skills)
                .FirstOrDefaultAsync(p => p.Id == profile.Id);

            Assert.NotNull(dbProfile);
            Assert.Single(dbProfile!.Skills!);
            Assert.Equal("Python", dbProfile.Skills!.First().Name);
            Assert.Equal(SkillProficiency.Advanced, dbProfile.Skills!.First().Proficiency);
        }
    }
}
