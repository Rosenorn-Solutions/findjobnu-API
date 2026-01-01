using FindjobnuService.Repositories.Context;
using FindjobnuService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace FindjobnuTesting.CvReadability
{
    public class CvServiceTests
    {
        private CvService CreateService(out FindjobnuContext context)
        {
            var options = new DbContextOptionsBuilder<FindjobnuContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            context = new FindjobnuContext(options);
            var profileService = new ProfileService(context);
            var logger = new Mock<ILogger<CvService>>().Object;
            return new CvService(context, profileService, logger);
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
    }
}
