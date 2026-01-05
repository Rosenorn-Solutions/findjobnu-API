using System.Collections.Generic;
using System.Linq;
using FindjobnuService.Repositories.Context;
using FindjobnuService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using SharedInfrastructure.Skills;

namespace FindjobnuTesting.CvReadability
{
    public class CvServiceTests
    {
        private CvService CreateService(out FindjobnuContext context)
        {
            var dbName = Guid.NewGuid().ToString();
            var options = new DbContextOptionsBuilder<FindjobnuContext>()
                .UseInMemoryDatabase(databaseName: dbName)
                .Options;
            context = new FindjobnuContext(options);

            // Seed canonical skills and synonyms for tests
            SeedSkillTaxonomy(context);

            var profileService = new ProfileService(context);

            // Create DbContextFactory for SkillTaxonomy
            var factoryOptions = new DbContextOptionsBuilder<FindjobnuContext>()
                .UseInMemoryDatabase(databaseName: dbName)
                .Options;
            var contextFactory = new TestDbContextFactory(factoryOptions);
            var cache = new MemoryCache(new MemoryCacheOptions());
            var taxonomy = new SkillTaxonomy(contextFactory, cache);

            var logger = new Mock<ILogger<CvService>>().Object;
            return new CvService(context, profileService, taxonomy, logger);
        }

        private static void SeedSkillTaxonomy(FindjobnuContext context)
        {
            if (context.CanonicalSkills.Any()) return;

            var csharp = new CanonicalSkill { Name = "C#", Slug = "csharp", ExternalId = Guid.NewGuid() };
            var sql = new CanonicalSkill { Name = "SQL", Slug = "sql", ExternalId = Guid.NewGuid() };
            var python = new CanonicalSkill { Name = "Python", Slug = "python", ExternalId = Guid.NewGuid() };
            var js = new CanonicalSkill { Name = "JavaScript", Slug = "javascript", ExternalId = Guid.NewGuid() };
            var customerService = new CanonicalSkill { Name = "Customer Service", Slug = "customer-service", ExternalId = Guid.NewGuid() };

            context.CanonicalSkills.AddRange(csharp, sql, python, js, customerService);
            context.SaveChanges();

            // Add synonyms
            context.SkillSynonyms.AddRange(
                new SkillSynonym { Synonym = "csharp", CanonicalSkillId = csharp.Id },
                new SkillSynonym { Synonym = "c-sharp", CanonicalSkillId = csharp.Id },
                new SkillSynonym { Synonym = "js", CanonicalSkillId = js.Id },
                new SkillSynonym { Synonym = "kundeservice", CanonicalSkillId = customerService.Id }
            );
            context.SaveChanges();
        }

        private sealed class TestDbContextFactory : IDbContextFactory<FindjobnuContext>
        {
            private readonly DbContextOptions<FindjobnuContext> _options;

            public TestDbContextFactory(DbContextOptions<FindjobnuContext> options)
            {
                _options = options;
            }

            public FindjobnuContext CreateDbContext()
            {
                return new FindjobnuContext(_options);
            }
        }

        private IFormFile MakeFile(string name, byte[] content, string contentType = "application/pdf")
        {
            return new FormFile(new MemoryStream(content), 0, content.Length, name, name)
            {
                Headers = new HeaderDictionary(),
                ContentType = contentType
            };
        }

        [Fact]
        public async Task AnalyzeAsync_Throws_WhenFileNull()
        {
            var svc = CreateService(out _);
            await Assert.ThrowsAsync<ArgumentException>(() => svc.AnalyzeAsync(null!, default));
        }

        [Fact]
        public async Task AnalyzeAsync_Throws_WhenFileTooLarge()
        {
            var svc = CreateService(out _);
            var big = new byte[10 * 1024 * 1024 + 1];
            var file = MakeFile("big.pdf", big);
            await Assert.ThrowsAsync<ArgumentException>(() => svc.AnalyzeAsync(file, default));
        }

        [Fact]
        public async Task ImportToProfileAsync_CreatesProfileFromPdf()
        {
            var svc = CreateService(out var ctx);
            var pdfContent = "%PDF-1.4\nJohn Doe\nPhone: +45 12345678\nSkills: C#, SQL\nExperience\nAcme - Developer\nDid things\n\nEducation\nUni - BSc\n%%EOF";
            var file = MakeFile("cv.pdf", System.Text.Encoding.ASCII.GetBytes(pdfContent));

            var result = await svc.ImportToProfileAsync("user1", file, default);

            Assert.NotNull(result);
            Assert.Equal("user1", result.Profile.UserId);
            Assert.NotNull(result.Profile.Skills);
            Assert.Contains(result.Profile.Skills!, s => s.Name.Contains("C#"));

            var profile = ctx.Profiles.Include(p => p.Skills).FirstOrDefault(p => p.UserId == "user1");
            Assert.NotNull(profile);
            Assert.NotEmpty(profile!.Skills!);
        }

        [Fact]
        public async Task ImportToProfileAsync_NormalizesSynonymsAndDeduplicates()
        {
            var svc = CreateService(out var ctx);
            var pdfContent = "%PDF-1.4\nJane Doe\nSkills: JavaScript, js, kundeservice\n%%EOF";
            var file = MakeFile("cv.pdf", System.Text.Encoding.ASCII.GetBytes(pdfContent));

            var result = await svc.ImportToProfileAsync("user2", file, default);

            Assert.NotNull(result);
            var skills = result.Profile.Skills!;
            Assert.Equal(2, skills.Count);
            Assert.Contains(skills, s => s.Name == "JavaScript");
            Assert.Contains(skills, s => s.Name == "Customer Service");

            var profile = ctx.Profiles.Include(p => p.Skills).FirstOrDefault(p => p.UserId == "user2");
            Assert.NotNull(profile);
            Assert.Equal(2, profile!.Skills!.Count);
        }

        [Fact]
        public async Task AnalyzeAsync_ReturnsSummaryFlags()
        {
            var svc = CreateService(out _);
            var pdfContent = "%PDF-1.4\nEmail: test@example.com\nPhone: +45 12345678\nExperience\n* Did something\nSkills: JavaScript\n%%EOF";
            var file = MakeFile("cv.pdf", System.Text.Encoding.ASCII.GetBytes(pdfContent));

            var result = await svc.AnalyzeAsync(file, default);

            Assert.NotNull(result);
            Assert.True(result.ReadabilityScore > 0);
            Assert.True(result.Summary.HasEmail);
            Assert.True(result.Summary.HasPhone);
            Assert.True(result.Summary.BulletCount > 0);
            Assert.True(result.Summary.MatchedSections > 0);
        }
    }
}
