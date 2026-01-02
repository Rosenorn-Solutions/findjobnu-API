using FindjobnuService.Models;
using FindjobnuService.Repositories.Context;
using FindjobnuService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace FindjobnuTesting
{
    public class NewsletterServiceTests
    {
        private static FindjobnuContext GetContext()
        {
            var options = new DbContextOptionsBuilder<FindjobnuContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            return new FindjobnuContext(options);
        }

        [Fact]
        public async Task SubscribeAsync_AddsNewEmail_WhenNotExisting()
        {
            using var context = GetContext();
            var logger = new Mock<ILogger<NewsletterService>>().Object;
            var service = new NewsletterService(context, logger);

            var result = await service.SubscribeAsync("test@example.com");

            Assert.True(result.Success);
            Assert.False(result.AlreadySubscribed);
            Assert.Equal(1, await context.NewsletterSubscriptions.CountAsync());
        }

        [Fact]
        public async Task SubscribeAsync_ReturnsAlreadySubscribed_WhenDuplicate()
        {
            using var context = GetContext();
            context.NewsletterSubscriptions.Add(new NewsletterSubscription { Email = "existing@example.com" });
            await context.SaveChangesAsync();

            var logger = new Mock<ILogger<NewsletterService>>().Object;
            var service = new NewsletterService(context, logger);

            var result = await service.SubscribeAsync("existing@example.com");

            Assert.True(result.Success);
            Assert.True(result.AlreadySubscribed);
            Assert.Equal(1, await context.NewsletterSubscriptions.CountAsync());
        }

        [Fact]
        public async Task SubscribeAsync_Throws_WhenEmailInvalid()
        {
            using var context = GetContext();
            var logger = new Mock<ILogger<NewsletterService>>().Object;
            var service = new NewsletterService(context, logger);

            await Assert.ThrowsAsync<ArgumentException>(() => service.SubscribeAsync("not-an-email"));
        }

        [Fact]
        public async Task SubscribeAsync_DedupesCaseInsensitive()
        {
            using var context = GetContext();
            context.NewsletterSubscriptions.Add(new NewsletterSubscription { Email = "Test@Example.com" });
            await context.SaveChangesAsync();

            var logger = new Mock<ILogger<NewsletterService>>().Object;
            var service = new NewsletterService(context, logger);

            var result = await service.SubscribeAsync("test@example.com ");

            Assert.True(result.AlreadySubscribed);
            Assert.Equal(1, await context.NewsletterSubscriptions.CountAsync());
        }
    }
}
