using AuthService.Data;
using AuthService.Entities;
using AuthService.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using SharedInfrastructure.Email;

namespace FindjobnuTesting
{
    public class AuthServiceTests
    {
        private ApplicationDbContext GetInMemoryDbContext()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            return new ApplicationDbContext(options);
        }

        private AuthService.Services.AuthService GetServiceWithMocks(ApplicationDbContext dbContext,
            out Mock<UserManager<ApplicationUser>> userManagerMock,
            out Mock<SignInManager<ApplicationUser>> signInManagerMock,
            out Mock<IConfiguration> configurationMock)
        {
            var userStoreMock = new Mock<IUserStore<ApplicationUser>>();
            userManagerMock = new Mock<UserManager<ApplicationUser>>(
                userStoreMock.Object, null, null, null, null, null, null, null, null);

            var contextAccessorMock = new Mock<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
            var claimsFactoryMock = new Mock<IUserClaimsPrincipalFactory<ApplicationUser>>();
            signInManagerMock = new Mock<SignInManager<ApplicationUser>>(
                userManagerMock.Object,
                contextAccessorMock.Object,
                claimsFactoryMock.Object,
                null, null, null, null);

            var loggerMock = new Mock<Microsoft.Extensions.Logging.ILogger<AuthService.Services.AuthService>>();
            var emailSenderMock = new Mock<IEmailSender>();

            configurationMock = new Mock<IConfiguration>();
            configurationMock.Setup(x => x.GetSection("JwtSettings")["SecretKey"])
                .Returns("THIS_IS_A_VERY_LONG_AND_SECURE_KEY_THAT_SHOULD_BE_AT_LEAST_32_BYTES_FOR_PRODUCTION_ENVIRONMENTS");
            configurationMock.Setup(x => x.GetSection("JwtSettings")["Issuer"]).Returns("issuer");
            configurationMock.Setup(x => x.GetSection("JwtSettings")["Audience"]).Returns("audience");
            configurationMock.Setup(x => x.GetSection("JwtSettings")["AccessTokenExpirationMinutes"]).Returns("60");
            configurationMock.Setup(x => x.GetSection("JwtSettings")["RefreshTokenExpirationDays"]).Returns("7");
            configurationMock.Setup(x => x["Domain"]).Returns("testdomain.com");

            return new AuthService.Services.AuthService(
                userManagerMock.Object,
                signInManagerMock.Object,
                configurationMock.Object,
                dbContext,
                loggerMock.Object,
                emailSenderMock.Object);
        }

        [Fact]
        public async Task RegisterAsync_Success_ReturnsAuthResponse()
        {
            using var dbContext = GetInMemoryDbContext();
            var service = GetServiceWithMocks(dbContext, out var userManagerMock, out _, out var configurationMock);

            var request = new RegisterRequest { Email = "test@example.com", Password = "Password123", Phone = "1234567890" };
            var user = new ApplicationUser { Id = "user1", Email = request.Email, UserName = request.Email, PhoneNumber = request.Phone };

            userManagerMock.Setup(x => x.CreateAsync(It.IsAny<ApplicationUser>(), request.Password))
                .ReturnsAsync(IdentityResult.Success);
            userManagerMock.Setup(x => x.GenerateEmailConfirmationTokenAsync(It.IsAny<ApplicationUser>()))
                .ReturnsAsync("token");
            userManagerMock.Setup(x => x.GetRolesAsync(It.IsAny<ApplicationUser>()))
                .ReturnsAsync(new List<string>());

            var result = await service.RegisterAsync(request);

            Assert.NotNull(result);
            Assert.True(result.Success);
            Assert.NotNull(result.AuthResponse);
            Assert.Equal(request.Email, result.AuthResponse.Email);
        }

        [Fact]
        public async Task RegisterAsync_Failure_ReturnsErrorMessage()
        {
            using var dbContext = GetInMemoryDbContext();
            var service = GetServiceWithMocks(dbContext, out var userManagerMock, out _, out _);

            var request = new RegisterRequest { Email = "fail@example.com", Password = "Password123" };
            var identityError = new IdentityError { Description = "Error" };
            var failedResult = IdentityResult.Failed(identityError);

            userManagerMock.Setup(x => x.CreateAsync(It.IsAny<ApplicationUser>(), request.Password))
                .ReturnsAsync(failedResult);

            var result = await service.RegisterAsync(request);

            Assert.NotNull(result);
            Assert.False(result.Success);
            Assert.NotNull(result.ErrorMessage);
            Assert.Contains("Error", result.ErrorMessage);
        }

        [Fact]
        public async Task LoginAsync_Success_ReturnsAuthResponse()
        {
            using var dbContext = GetInMemoryDbContext();
            var service = GetServiceWithMocks(dbContext, out var userManagerMock, out var signInManagerMock, out var configurationMock);

            var request = new LoginRequest { Email = "test@example.com", Password = "Password123" };
            var user = new ApplicationUser { Id = "user1", Email = request.Email, UserName = request.Email };

            userManagerMock.Setup(x => x.FindByEmailAsync(request.Email)).ReturnsAsync(user);
            signInManagerMock.Setup(x => x.CheckPasswordSignInAsync(user, request.Password, false))
                .ReturnsAsync(SignInResult.Success);
            userManagerMock.Setup(x => x.GetRolesAsync(user)).ReturnsAsync(new List<string>());

            var response = await service.LoginAsync(request);

            Assert.NotNull(response);
            Assert.Equal(request.Email, response.AuthResponse?.Email);
        }

        [Fact]
        public async Task LoginAsync_UserNotFound_ReturnsNull()
        {
            using var dbContext = GetInMemoryDbContext();
            var service = GetServiceWithMocks(dbContext, out var userManagerMock, out var signInManagerMock, out _);

            var request = new LoginRequest { Email = "notfound@example.com", Password = "Password123" };
            userManagerMock.Setup(x => x.FindByEmailAsync(request.Email)).ReturnsAsync((ApplicationUser)null);

            var response = await service.LoginAsync(request);

            Assert.Equal("No user exists with the given E-mail.", response.ErrorMessage);
        }

        [Fact]
        public async Task ConfirmEmailAsync_Success_ReturnsTrue()
        {
            using var dbContext = GetInMemoryDbContext();
            var service = GetServiceWithMocks(dbContext, out var userManagerMock, out _, out _);

            var userId = "user1";
            var token = "token";
            var user = new ApplicationUser { Id = userId };

            userManagerMock.Setup(x => x.FindByIdAsync(userId)).ReturnsAsync(user);
            userManagerMock.Setup(x => x.ConfirmEmailAsync(user, token)).ReturnsAsync(IdentityResult.Success);

            var result = await service.ConfirmEmailAsync(userId, token);

            Assert.True(result);
        }

        [Fact]
        public async Task ConfirmEmailAsync_UserNotFound_ReturnsFalse()
        {
            using var dbContext = GetInMemoryDbContext();
            var service = GetServiceWithMocks(dbContext, out var userManagerMock, out _, out _);

            var userId = "user2";
            var token = "token";
            userManagerMock.Setup(x => x.FindByIdAsync(userId)).ReturnsAsync((ApplicationUser)null);

            var result = await service.ConfirmEmailAsync(userId, token);

            Assert.False(result);
        }

        [Fact]
        public async Task RefreshTokenAsync_ReturnsNull_WhenPrincipalIsNull()
        {
            using var dbContext = GetInMemoryDbContext();
            var service = GetServiceWithMocks(dbContext, out var userManagerMock, out _, out _);
            var request = new TokenRefreshRequest { AccessToken = "invalid", RefreshToken = "refresh" };
            // Simulate GetPrincipalFromExpiredToken returns null by passing an invalid token
            var result = await service.RefreshTokenAsync(request);
            Assert.Null(result);
        }

        [Fact]
        public async Task RevokeRefreshTokenAsync_ReturnsFalse_WhenTokenNotFound()
        {
            using var dbContext = GetInMemoryDbContext();
            var service = GetServiceWithMocks(dbContext, out _, out _, out _);
            var result = await service.RevokeRefreshTokenAsync("userId", "notfoundtoken");
            Assert.False(result);
        }

        [Fact]
        public async Task RevokeRefreshTokenAsync_ReturnsTrue_WhenTokenIsActive()
        {
            using var dbContext = GetInMemoryDbContext();
            var service = GetServiceWithMocks(dbContext, out _, out _, out _);
            var token = new RefreshToken
            {
                Token = "token1",
                UserId = "userId",
                Expires = DateTime.UtcNow.AddDays(1),
                Created = DateTime.UtcNow
            };
            dbContext.RefreshTokens.Add(token);
            dbContext.SaveChanges();
            var result = await service.RevokeRefreshTokenAsync("userId", "token1");
            Assert.True(result);
            var dbToken = await dbContext.RefreshTokens.FirstOrDefaultAsync(t => t.Token == "token1");
            Assert.NotNull(dbToken);
            Assert.NotNull(dbToken.Revoked);
        }
    }
}