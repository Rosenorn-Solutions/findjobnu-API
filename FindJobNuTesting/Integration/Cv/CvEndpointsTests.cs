using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace FindjobnuTesting.Integration.Cv
{
    public class CvEndpointsTests : IClassFixture<FindjobnuApiFactory>
    {
        private readonly HttpClient _client;

        public CvEndpointsTests(FindjobnuApiFactory factory)
        {
            _client = factory.CreateClient();
        }

        [Fact]
        public async Task ImportCv_Unauthorized_WhenNoAuth()
        {
            var pdfContent = "%PDF-1.4\nJohn Doe\n%%EOF";
            var content = new MultipartFormDataContent();
            var bytes = Encoding.ASCII.GetBytes(pdfContent);
            var fileContent = new ByteArrayContent(bytes);
            fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
            content.Add(fileContent, "file", "cv.pdf");

            var response = await _client.PostAsync("/api/cv/import", content);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }
}
