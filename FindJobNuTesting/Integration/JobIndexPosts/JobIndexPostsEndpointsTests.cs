using System.Net;

namespace FindjobnuTesting.Integration.JobIndexPosts
{
    public class JobIndexPostsEndpointsTests : IClassFixture<FindjobnuApiFactory>
    {
        private readonly HttpClient _client;
        public JobIndexPostsEndpointsTests(FindjobnuApiFactory factory)
        {
            _client = factory.CreateClient();
        }

        [Fact]
        public async Task GetAll_ReturnsNoContent_WhenEmpty()
        {
            var response = await _client.GetAsync("/api/jobindexposts?page=1&pageSize=10");
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        [Fact]
        public async Task Search_ReturnsNoContent_WhenEmpty()
        {
            var response = await _client.GetAsync("/api/jobindexposts/search?location=nowhere&page=1");
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        [Fact]
        public async Task GetById_ReturnsNoContent_WhenNotFound()
        {
            var response = await _client.GetAsync("/api/jobindexposts/9999");
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        [Fact]
        public async Task Saved_ReturnsUnauthorized_WhenNoAuth()
        {
            var response = await _client.GetAsync("/api/jobindexposts/saved?page=1");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task Recommended_ReturnsUnauthorized_WhenNoAuth()
        {
            var response = await _client.GetAsync("/api/jobindexposts/recommended-jobs?page=1");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task GetAll_ReturnsBadRequest_ForInvalidPaging()
        {
            var response = await _client.GetAsync("/api/jobindexposts?page=0&pageSize=5000");
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task Search_ReturnsBadRequest_ForInvalidPaging()
        {
            var response = await _client.GetAsync("/api/jobindexposts/search?page=0&pageSize=5000");
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }
}
