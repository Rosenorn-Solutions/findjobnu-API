using AuthService.Data;
using AuthService.Entities;
using AuthService.Models;
using AuthService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using Moq.Protected;
using System.Net;

namespace FindjobnuTesting
{
    public class LinkedInAuthServiceTests
    {
        private ApplicationDbContext GetInMemoryDbContext()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            return new ApplicationDbContext(options);
        }

        private LinkedInAuthService GetServiceWithMocks(
            out Mock<UserManager<ApplicationUser>> userManagerMock,
            out Mock<SignInManager<ApplicationUser>> signInManagerMock,
            out Mock<IConfiguration> configurationMock,
            out Mock<IHttpClientFactory> httpClientFactoryMock,
            out Mock<IAuthService> authServiceMock)
        {
            var userStoreMock = new Mock<IUserStore<ApplicationUser>>();
            userManagerMock = new Mock<UserManager<ApplicationUser>>(
                userStoreMock.Object, null, null, null, null, null, null, null, null);

            var contextAccessorMock = new Mock<IHttpContextAccessor>();
            var claimsFactoryMock = new Mock<IUserClaimsPrincipalFactory<ApplicationUser>>();
            signInManagerMock = new Mock<SignInManager<ApplicationUser>>(
                userManagerMock.Object,
                contextAccessorMock.Object,
                claimsFactoryMock.Object,
                null, null, null, null);

            configurationMock = new Mock<IConfiguration>();
            configurationMock.Setup(x => x["LinkedInOAuth:ClientId"]).Returns("clientId");
            configurationMock.Setup(x => x["LinkedInOAuth:ClientSecret"]).Returns("clientSecret");
            configurationMock.Setup(x => x["LinkedInOAuth:RedirectUri"]).Returns("https://localhost/callback");
            configurationMock.Setup(x => x["LinkedInOAuth:FindJobNuFrontendUrl"]).Returns("https://frontend/findjobnu");
            configurationMock.Setup(x => x["LinkedInOAuth:PasswordSecretKey"]).Returns("supersecretkey");

            httpClientFactoryMock = new Mock<IHttpClientFactory>();
            authServiceMock = new Mock<IAuthService>();

            return new LinkedInAuthService(
                configurationMock.Object,
                authServiceMock.Object,
                httpClientFactoryMock.Object,
                signInManagerMock.Object,
                userManagerMock.Object);
        }

        [Fact]
        public void GetLoginUrl_ReturnsValidUrl()
        {
            var service = GetServiceWithMocks(out _, out _, out var configMock, out _, out _);
            var url = service.GetLoginUrl();
            Assert.Contains("client_id=clientId", url);
            Assert.Contains("redirect_uri=https%3A%2F%2Flocalhost%2Fcallback", url);
            Assert.Contains("scope=openid", url);
        }

        [Fact]
        public async Task HandleCallbackAsync_ReturnsBadRequest_WhenCodeMissing()
        {
            var service = GetServiceWithMocks(out _, out _, out _, out _, out _);
            var context = new DefaultHttpContext();
            var result = await service.HandleCallbackAsync(context);
            var badRequest = Assert.IsType<BadRequest<string>>(result);
            Assert.Contains("Missing code", badRequest.Value.ToString());
        }

        [Fact]
        public async Task HandleCallbackAsync_ReturnsBadRequest_WhenAccessTokenFails()
        {
            var service = GetServiceWithMocks(out _, out _, out var configMock, out var httpClientFactoryMock, out _);
            var context = new DefaultHttpContext();
            context.Request.QueryString = new QueryString("?code=abc");

            var httpMock = new Mock<HttpMessageHandler>();
            httpMock.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.BadRequest));
            var client = new HttpClient(httpMock.Object);
            httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);

            var result = await service.HandleCallbackAsync(context);
            var badRequest = Assert.IsType<BadRequest<string>>(result);
            Assert.Contains("Failed to get access token", badRequest.Value.ToString());
        }

        [Fact]
        public async Task HandleCallbackAsync_ReturnsBadRequest_WhenUserInfoFails()
        {
            var service = GetServiceWithMocks(out _, out _, out var configMock, out var httpClientFactoryMock, out _);
            var context = new DefaultHttpContext();
            context.Request.QueryString = new QueryString("?code=abc");

            // First call: token (success), Second: userinfo (fail)
            var handler = new Mock<HttpMessageHandler>();
            handler.Protected()
                .SetupSequence<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"token\"}")
                })
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.BadRequest));
            var client = new HttpClient(handler.Object);
            httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);

            var result = await service.HandleCallbackAsync(context);
            var badRequest = Assert.IsType<BadRequest<string>>(result);
            Assert.Contains("Failed to fetch LinkedIn user info", badRequest.Value.ToString());
        }

        [Fact]
        public async Task HandleCallbackAsync_ReturnsBadRequest_WhenEmailMissing()
        {
            var service = GetServiceWithMocks(out _, out _, out var configMock, out var httpClientFactoryMock, out _);
            var context = new DefaultHttpContext();
            context.Request.QueryString = new QueryString("?code=abc");

            // Token, userinfo, profile all succeed, but userinfo missing email
            var handler = new Mock<HttpMessageHandler>();
            handler.Protected()
                .SetupSequence<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"token\"}")
                })
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"given_name\":\"John\",\"family_name\":\"Doe\",\"sub\":\"123\"}")
                })
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"vanityName\":\"john-doe\",\"localizedHeadline\":\"Engineer\"}")
                });
            var client = new HttpClient(handler.Object);
            httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);

            var result = await service.HandleCallbackAsync(context);
            var badRequest = Assert.IsType<BadRequest<string>>(result);
            Assert.Contains("Email not found", badRequest.Value.ToString());
        }

        [Fact]
        public async Task HandleCallbackAsync_ExistingUser_SuccessfulSignIn_Redirects()
        {
            var service = GetServiceWithMocks(out var userManagerMock, out var signInManagerMock, out var configMock, out var httpClientFactoryMock, out var authServiceMock);
            var context = new DefaultHttpContext();
            context.Request.QueryString = new QueryString("?code=abc");

            // Token, userinfo, profile all succeed
            var handler = new Mock<HttpMessageHandler>();
            handler.Protected()
                .SetupSequence<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"token\"}")
                })
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"email\":\"test@example.com\",\"given_name\":\"John\",\"family_name\":\"Doe\",\"sub\":\"123\"}")
                })
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"vanityName\":\"john-doe\",\"localizedHeadline\":\"Engineer\"}")
                });
            var client = new HttpClient(handler.Object);
            httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);

            var user = new ApplicationUser { Id = "user1", Email = "test@example.com", IsLinkedInUser = true };
            userManagerMock.Setup(x => x.FindByEmailAsync("test@example.com")).ReturnsAsync(user);
            signInManagerMock.Setup(x => x.CheckPasswordSignInAsync(user, It.IsAny<string>(), false)).ReturnsAsync(SignInResult.Success);
            authServiceMock.Setup(x => x.LoginAsync(It.IsAny<LoginRequest>(), true)).ReturnsAsync(new LoginResult
            {
                Success = true,
                AuthResponse = new AuthResponse
                {
                    UserId = "user1",
                    Email = "test@example.com",
                    FirstName = "John",
                    LastName = "Doe",
                    AccessToken = "access",
                    RefreshToken = "refresh",
                    AccessTokenExpiration = DateTime.UtcNow.AddHours(1),
                    LinkedInId = "123"
                }
            });

            var result = await service.HandleCallbackAsync(context);
            var redirect = Assert.IsType<RedirectHttpResult>(result);
            Assert.Contains("test%40example.com", redirect.Url);
            Assert.Contains("accessToken=access", redirect.Url);
        }

        [Fact]
        public async Task HandleCallbackAsync_ReturnsBadRequest_WhenNameMissing()
        {
            var service = GetServiceWithMocks(out _, out _, out var configMock, out var httpClientFactoryMock, out _);
            var context = new DefaultHttpContext();
            context.Request.QueryString = new QueryString("?code=abc");

            var handler = new Mock<HttpMessageHandler>();
            handler.Protected()
                .SetupSequence<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"token\"}")
                })
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"email\":\"test@example.com\",\"sub\":\"123\"}")
                })
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"vanityName\":\"john-doe\",\"localizedHeadline\":\"Engineer\"}")
                });
            var client = new HttpClient(handler.Object);
            httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);

            var result = await service.HandleCallbackAsync(context);
            var badRequest = Assert.IsType<BadRequest<string>>(result);
            Assert.Contains("First name or last name not found", badRequest.Value.ToString());
        }

        [Fact]
        public async Task HandleCallbackAsync_NewUser_SuccessfulFlow_Redirects()
        {
            var service = GetServiceWithMocks(out var userManagerMock, out var signInManagerMock, out var configMock, out var httpClientFactoryMock, out var authServiceMock);
            var context = new DefaultHttpContext();
            context.Request.QueryString = new QueryString("?code=abc");

            var handler = new Mock<HttpMessageHandler>();
            handler.Protected()
                .SetupSequence<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"token\"}")
                })
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"email\":\"new@example.com\",\"given_name\":\"Jane\",\"family_name\":\"Doe\",\"sub\":\"456\"}")
                })
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"vanityName\":\"jane-doe\",\"localizedHeadline\":\"Engineer\"}")
                });
            var client = new HttpClient(handler.Object);
            httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);

            userManagerMock.Setup(x => x.FindByEmailAsync("new@example.com")).ReturnsAsync((ApplicationUser?)null);
            userManagerMock.Setup(x => x.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
                .ReturnsAsync(IdentityResult.Success);
            signInManagerMock.Setup(x => x.CheckPasswordSignInAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>(), false))
                .ReturnsAsync(SignInResult.Success);
            authServiceMock.Setup(x => x.LoginAsync(It.IsAny<LoginRequest>(), true)).ReturnsAsync(new LoginResult
            {
                Success = true,
                AuthResponse = new AuthResponse
                {
                    UserId = "user-new",
                    Email = "new@example.com",
                    FirstName = "Jane",
                    LastName = "Doe",
                    AccessToken = "access-token",
                    RefreshToken = "refresh-token",
                    AccessTokenExpiration = DateTime.UtcNow.AddHours(1),
                    LinkedInId = "456"
                }
            });

            var result = await service.HandleCallbackAsync(context);
            var redirect = Assert.IsType<RedirectHttpResult>(result);
            Assert.Contains("new%40example.com", redirect.Url);
            Assert.Contains("access-token", redirect.Url);
            Assert.Contains("refresh-token", redirect.Url);
        }
    }
}
