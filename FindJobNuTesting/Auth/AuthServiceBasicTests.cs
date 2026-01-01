using AuthService.Data;
using AuthService.Entities;
using AuthService.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedInfrastructure.Email;

namespace FindjobnuTesting.Auth
{
    public class AuthServiceBasicTests
    {
        private (AuthService.Services.AuthService svc, ApplicationDbContext dbContext, UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser>) Create()
        {
            var services = new ServiceCollection();
            var configData = new Dictionary<string, string?>
            {
                {"JwtSettings:SecretKey", "supersecretkey1234567890asdasdasdasdasdasdasdasdasd"},
                {"JwtSettings:Issuer", "testIssuer"},
                {"JwtSettings:Audience", "testAudience"},
                {"JwtSettings:AccessTokenExpirationMinutes", "5"},
                {"JwtSettings:RefreshTokenExpirationDays", "7"}
            };
            IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(configData!).Build();

            services.AddSingleton(configuration);
            services.AddLogging();
            services.AddHttpContextAccessor();
            services.AddDbContext<ApplicationDbContext>(opts => opts.UseInMemoryDatabase(System.Guid.NewGuid().ToString()));
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            services.AddAuthentication(); // required for SignInManager dependencies
            services.AddIdentityCore<ApplicationUser>(o => { })
                .AddRoles<IdentityRole>()
                .AddSignInManager() // register SignInManager
                .AddEntityFrameworkStores<ApplicationDbContext>()
                .AddDefaultTokenProviders();
            services.AddSingleton<IEmailSender, NoopEmailSender>();

            var sp = services.BuildServiceProvider();
            var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var signInManager = scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<AuthService.Services.AuthService>>();
            var emailSender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
            var svc = new AuthService.Services.AuthService(userManager, signInManager, configuration, db, logger, emailSender);
            return (svc, db, userManager, signInManager);
        }

        [Fact]
        public async System.Threading.Tasks.Task RegisterAsync_Fails_WithWeakPassword()
        {
            var (svc, _, _, _) = Create();
            var result = await svc.RegisterAsync(new RegisterRequest { Email = "a@b.com", Password = "123", FirstName = "A", LastName = "B" });
            Assert.False(result.Success);
            Assert.NotNull(result.ErrorMessage);
        }

        [Fact]
        public async System.Threading.Tasks.Task RegisterAndLogin_Succeeds()
        {
            var (svc, _, _, _) = Create();
            var reg = await svc.RegisterAsync(new RegisterRequest { Email = "c@d.com", Password = "Str0ngP@ss!", FirstName = "C", LastName = "D" });
            Assert.True(reg.Success);
            Assert.NotNull(reg.AuthResponse);
            var login = await svc.LoginAsync(new LoginRequest { Email = "c@d.com", Password = "Str0ngP@ss!" });
            Assert.True(login.Success);
            Assert.NotNull(login.AuthResponse);
        }

        [Fact]
        public async System.Threading.Tasks.Task Login_Fails_WithWrongPassword()
        {
            var (svc, _, _, _) = Create();
            var reg = await svc.RegisterAsync(new RegisterRequest { Email = "x@y.com", Password = "Str0ngP@ss!", FirstName = "X", LastName = "Y" });
            Assert.True(reg.Success);
            var login = await svc.LoginAsync(new LoginRequest { Email = "x@y.com", Password = "bad" });
            Assert.False(login.Success);
        }

        [Fact]
        public async System.Threading.Tasks.Task RefreshToken_Fails_WithInvalidTokens()
        {
            var (svc, _, _, _) = Create();
            var refresh = await svc.RefreshTokenAsync(new TokenRefreshRequest { AccessToken = "invalid", RefreshToken = "invalid" });
            Assert.Null(refresh);
        }

        [Fact]
        public async System.Threading.Tasks.Task RevokeRefreshToken_Fails_WhenNotFound()
        {
            var (svc, _, _, _) = Create();
            var ok = await svc.RevokeRefreshTokenAsync("nouser", "notoken");
            Assert.False(ok);
        }

        [Fact]
        public async System.Threading.Tasks.Task ChangePassword_Succeeds_AndRevokesTokens()
        {
            var (svc, _, userManager, _) = Create();
            var reg = await svc.RegisterAsync(new RegisterRequest { Email = "pw@t.com", Password = "Str0ngP@ss!", FirstName = "T", LastName = "U" });
            Assert.True(reg.Success);
            var user = await userManager.FindByEmailAsync("pw@t.com");
            Assert.NotNull(user);

            var res = await svc.UpdatePasswordAsync(user!.Id, "Str0ngP@ss!", "An0ther$trong1");
            Assert.True(res.Succeeded);

            var oldLogin = await svc.LoginAsync(new LoginRequest { Email = "pw@t.com", Password = "Str0ngP@ss!" });
            Assert.False(oldLogin.Success);
            var newLogin = await svc.LoginAsync(new LoginRequest { Email = "pw@t.com", Password = "An0ther$trong1" });
            Assert.True(newLogin.Success);
        }

        [Fact]
        public async System.Threading.Tasks.Task ConfirmChangeEmail_Succeeds()
        {
            var (svc, _, userManager, _) = Create();
            var reg = await svc.RegisterAsync(new RegisterRequest { Email = "old@mail.com", Password = "Str0ngP@ss!", FirstName = "F", LastName = "L" });
            Assert.True(reg.Success);
            var user = await userManager.FindByEmailAsync("old@mail.com");
            Assert.NotNull(user);

            var newEmail = "new@mail.com";
            var token = await userManager.GenerateChangeEmailTokenAsync(user!, newEmail);
            var res = await svc.ConfirmChangeEmailAsync(user!.Id, newEmail, token);
            Assert.True(res.Succeeded);

            var updated = await userManager.FindByEmailAsync(newEmail);
            Assert.NotNull(updated);
            Assert.Equal(newEmail, updated!.Email);
        }

        [Fact]
        public async System.Threading.Tasks.Task DisableAccount_LocksOutUser()
        {
            var (svc, _, userManager, _) = Create();
            var reg = await svc.RegisterAsync(new RegisterRequest { Email = "disable@test.com", Password = "Str0ngP@ss!", FirstName = "D", LastName = "S" });
            Assert.True(reg.Success);
            var user = await userManager.FindByEmailAsync("disable@test.com");
            Assert.NotNull(user);

            var res = await svc.DisableAccountAsync(user!.Id, "Str0ngP@ss!");
            Assert.True(res.Succeeded);

            var refreshed = await userManager.FindByIdAsync(user.Id);
            Assert.True(refreshed!.LockoutEnabled);
            Assert.True(refreshed.LockoutEnd.HasValue);
        }

        private class NoopEmailSender : IEmailSender
        {
            public Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default) => Task.CompletedTask;
        }
    }
}
