using FindjobnuService.DTOs.Responses;
using System.Net;
using System.Net.Http.Json;

namespace FindjobnuTesting.Integration.Newsletter
{
    public class NewsletterEndpointsTests : IClassFixture<FindjobnuApiFactory>
    {
        private readonly HttpClient _client;

        public NewsletterEndpointsTests(FindjobnuApiFactory factory)
        {
            _client = factory.CreateClient();
        }

        [Fact]
        public async Task Subscribe_ReturnsOk_AndPreventsDuplicates()
        {
            var payload = new { email = "newsletter@test.com" };
            var first = await _client.PostAsJsonAsync("/api/newsletter/subscribe", payload);
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            var firstResponse = await first.Content.ReadFromJsonAsync<NewsletterSubscribeResponse>();
            Assert.NotNull(firstResponse);
            Assert.True(firstResponse!.Success);
            Assert.False(firstResponse.AlreadySubscribed);

            var second = await _client.PostAsJsonAsync("/api/newsletter/subscribe", payload);
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);
            var secondResponse = await second.Content.ReadFromJsonAsync<NewsletterSubscribeResponse>();
            Assert.NotNull(secondResponse);
            Assert.True(secondResponse!.AlreadySubscribed);
        }

        [Fact]
        public async Task Subscribe_ReturnsBadRequest_ForInvalidEmail()
        {
            var payload = new { email = "bad-email" };
            var response = await _client.PostAsJsonAsync("/api/newsletter/subscribe", payload);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }
}
