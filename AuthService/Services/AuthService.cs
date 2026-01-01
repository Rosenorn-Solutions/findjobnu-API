using AuthService.Data;
using AuthService.Entities;
using AuthService.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using SharedInfrastructure.Email;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace AuthService.Services
{
    public class AuthService : IAuthService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IConfiguration _configuration;
        private readonly ApplicationDbContext _dbContext;
        private readonly ILogger<AuthService> _logger;
        private readonly IEmailSender _emailSender;

        public AuthService(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager, IConfiguration configuration, ApplicationDbContext dbContext, ILogger<AuthService> logger, IEmailSender emailSender)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _configuration = configuration;
            _dbContext = dbContext;
            _logger = logger;
            _emailSender = emailSender;
        }

        public async Task<RegisterResult> RegisterAsync(RegisterRequest request, bool isLinkedInUser = false)
        {
            var user = new ApplicationUser
            {
                UserName = request.Email,
                Email = request.Email,
                PhoneNumber = request.Phone,
                FirstName = request.FirstName,
                LastName = request.LastName,
                IsLinkedInUser = isLinkedInUser,
                HasVerifiedLinkedIn = isLinkedInUser,
                LinkedInId = request.LinkedInId
            };
            var result = await _userManager.CreateAsync(user, request.Password);

            if (result.Succeeded)
            {
                // Fire-and-forget confirmation email without blocking registration flow
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
                        var confirmationLink = $"https://auth.findjob.nu/api/auth/confirm-email?userId={Uri.EscapeDataString(user.Id)}&token={Uri.EscapeDataString(token)}";
                        await _emailSender.SendAsync(user.Email!, "Confirm your email", $"Please confirm your account by clicking this link: <a href=\"{confirmationLink}\">Confirm Email</a>");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Email sending failed during registration");
                    }
                });

                var authResponse = await GenerateAuthResponseAsync(user);
                return new RegisterResult { Success = true, AuthResponse = authResponse };
            }

            string errorMessage = string.Join(" ", result.Errors.Select(e => e.Description));
            _logger.LogError("Registration Errors: {errorMessage}", errorMessage);
            return new RegisterResult { Success = false, ErrorMessage = errorMessage };
        }

        public async Task<LoginResult> LoginAsync(LoginRequest request, bool isLinkedInUser = false)
        {
            var user = await _userManager.FindByEmailAsync(request.Email);
            if (user == null)
                return new LoginResult { AuthResponse = null, ErrorMessage = "No user exists with the given E-mail.", Success = false };

            if (isLinkedInUser && !user.IsLinkedInUser)
                return new LoginResult { AuthResponse = null, ErrorMessage = "This user is not a LinkedIn user.", Success = false };

            if (!isLinkedInUser && user.IsLinkedInUser)
                return new LoginResult { AuthResponse = null, ErrorMessage = "This user is a LinkedIn user.", Success = false };

            var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: false);
            if (result.Succeeded)
            {
                var authResponse = await GenerateAuthResponseAsync(user);
                if (authResponse == null)
                    return new LoginResult { AuthResponse = null, ErrorMessage = "Failed to generate authentication response.", Success = false };
                return new LoginResult { AuthResponse = authResponse, Success = true };
            }
            return new LoginResult { AuthResponse = null, ErrorMessage = "Invalid credentials.", Success = false };
        }

        public async Task<AuthResponse?> RefreshTokenAsync(TokenRefreshRequest request)
        {
            var principal = GetPrincipalFromExpiredToken(request.AccessToken);
            if (principal == null) return null;

            var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return null;

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return null;

            var storedRefreshToken = await _dbContext.RefreshTokens.FirstOrDefaultAsync(rt => rt.Token == request.RefreshToken && rt.UserId == user.Id);
            if (storedRefreshToken == null || storedRefreshToken.Revoked != null || storedRefreshToken.Expires <= DateTime.UtcNow)
            {
                if (storedRefreshToken != null && storedRefreshToken.Revoked != null)
                {
                    await RevokeDescendantRefreshTokens(storedRefreshToken, user);
                }
                return null;
            }

            storedRefreshToken.Revoked = DateTime.UtcNow;
            storedRefreshToken.ReplacedByToken = GenerateRefreshToken();
            _dbContext.RefreshTokens.Update(storedRefreshToken);

            var newAuthResponse = await GenerateAuthResponseAsync(user, storedRefreshToken.ReplacedByToken);
            await _dbContext.SaveChangesAsync();
            return newAuthResponse;
        }

        public async Task<bool> RevokeRefreshTokenAsync(string userId, string refreshToken)
        {
            var storedRefreshToken = await _dbContext.RefreshTokens.FirstOrDefaultAsync(rt => rt.Token == refreshToken && rt.UserId == userId);
            if (storedRefreshToken == null || storedRefreshToken.Revoked != null || storedRefreshToken.Expires <= DateTime.UtcNow)
                return false;
            storedRefreshToken.Revoked = DateTime.UtcNow;
            _dbContext.RefreshTokens.Update(storedRefreshToken);
            await _dbContext.SaveChangesAsync();
            return true;
        }

        public async Task<bool> ConfirmEmailAsync(string userId, string token)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return false;
            var result = await _userManager.ConfirmEmailAsync(user, token);
            return result.Succeeded;
        }

        private async Task<AuthResponse> GenerateAuthResponseAsync(ApplicationUser user, string? existingRefreshToken = null)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id),
                new Claim(ClaimTypes.Email, user.Email!),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            var roles = await _userManager.GetRolesAsync(user);
            foreach (var role in roles) claims.Add(new Claim(ClaimTypes.Role, role));

            var jwtSettings = _configuration.GetSection("JwtSettings");
            var secretKey = jwtSettings["SecretKey"]!;
            var issuer = jwtSettings["Issuer"]!;
            var audience = jwtSettings["Audience"]!;
            var accessTokenExpirationMinutes = int.Parse(jwtSettings["AccessTokenExpirationMinutes"]!);
            var refreshTokenExpirationDays = int.Parse(jwtSettings["RefreshTokenExpirationDays"]!);

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var expires = DateTime.UtcNow.AddMinutes(accessTokenExpirationMinutes);

            var token = new JwtSecurityToken(issuer, audience, claims, expires: expires, signingCredentials: creds);
            var accessToken = new JwtSecurityTokenHandler().WriteToken(token);

            string refreshTokenValue = existingRefreshToken ?? GenerateRefreshToken();
            if (existingRefreshToken != null)
            {
                await _dbContext.RefreshTokens.AddAsync(new RefreshToken
                {
                    Token = refreshTokenValue,
                    Expires = DateTime.UtcNow.AddDays(refreshTokenExpirationDays),
                    UserId = user.Id,
                    Created = DateTime.UtcNow
                });
            }
            else
            {
                await _dbContext.RefreshTokens.AddAsync(new RefreshToken
                {
                    Token = refreshTokenValue,
                    Expires = DateTime.UtcNow.AddDays(refreshTokenExpirationDays),
                    UserId = user.Id,
                    Created = DateTime.UtcNow
                });
                await _dbContext.SaveChangesAsync();
            }

            return new AuthResponse
            {
                UserId = user.Id,
                Email = user.Email!,
                FirstName = user.FirstName,
                LastName = user.LastName,
                AccessToken = accessToken,
                RefreshToken = refreshTokenValue,
                AccessTokenExpiration = expires,
                LinkedInId = user.LinkedInId,
            };
        }

        private string GenerateRefreshToken()
        {
            var bytes = RandomNumberGenerator.GetBytes(64);
            return Convert.ToBase64String(bytes);
        }

        private ClaimsPrincipal? GetPrincipalFromExpiredToken(string token)
        {
            var parameters = new TokenValidationParameters
            {
                ValidateAudience = false,
                ValidateIssuer = false,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration.GetSection("JwtSettings")["SecretKey"]!)),
                ValidateLifetime = false
            };
            var handler = new JwtSecurityTokenHandler();
            try
            {
                var principal = handler.ValidateToken(token, parameters, out SecurityToken securityToken);
                if (securityToken is not JwtSecurityToken jwt || !jwt.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.InvariantCultureIgnoreCase))
                    throw new SecurityTokenException("Invalid token algorithm");
                return principal;
            }
            catch (SecurityTokenExpiredException)
            {
                var expired = handler.ReadJwtToken(token);
                var claims = new ClaimsIdentity(expired.Claims);
                if (!claims.HasClaim(c => c.Type == JwtRegisteredClaimNames.Jti))
                    claims.AddClaim(new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()));
                return new ClaimsPrincipal(claims);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Token validation error");
                return null;
            }
        }

        private async Task RevokeDescendantRefreshTokens(RefreshToken refreshToken, ApplicationUser user)
        {
            var tokens = await _dbContext.RefreshTokens
                .Where(rt => rt.UserId == user.Id && rt.Revoked == null && rt.Created > refreshToken.Created && rt.Expires > DateTime.UtcNow)
                .ToListAsync();
            foreach (var t in tokens) t.Revoked = DateTime.UtcNow;
            if (tokens.Count > 0) await _dbContext.SaveChangesAsync();
        }

        public async Task<Tuple<bool, string?>> IsLinkedInUserOrHasVerifiedTheirLinkedIn(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return new Tuple<bool, string?>(false, null);
            return (user.IsLinkedInUser || user.HasVerifiedLinkedIn)
                ? new Tuple<bool, string?>(true, user.LinkedInId)
                : new Tuple<bool, string?>(false, null);
        }

        public async Task<UserInformationResult> GetUserInformationAsync(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                return new UserInformationResult { Success = false, ErrorMessage = "User not found." };
            return new UserInformationResult { Success = true, UserInformation = MapToUserInformationDTO(user) };
        }

        private UserInformationDTO MapToUserInformationDTO(ApplicationUser user) => new()
        {
            Id = user.Id,
            Email = user.Email!,
            UserName = user.UserName ?? "",
            FirstName = user.FirstName,
            LastName = user.LastName,
            Phone = user.PhoneNumber,
            IsLinkedInUser = user.IsLinkedInUser,
            HasVerifiedLinkedIn = user.HasVerifiedLinkedIn,
            LinkedInId = user.LinkedInId ?? "",
            LinkedInProfileUrl = user.LinkedInProfileUrl,
            LinkedInHeadline = user.LinkedInHeadline,
            LastLinkedInSync = user.LastLinkedInSync,
            CreatedAt = user.CreatedAt
        };

        public async Task<IdentityResult> LockoutUserAsync(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return IdentityResult.Failed(new IdentityError { Description = "User not found." });
            user.LockoutEnd = DateTimeOffset.UtcNow.AddYears(100);
            user.LockoutEnabled = true;
            return await _userManager.UpdateAsync(user);
        }

        public async Task<IdentityResult> UpdatePasswordAsync(string userId, string oldPassword, string newPassword)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return IdentityResult.Failed(new IdentityError { Description = "User not found." });
            return await _userManager.ChangePasswordAsync(user, oldPassword, newPassword);
        }

        public async Task RevokeAllRefreshTokensAsync(string userId)
        {
            var now = DateTime.UtcNow;
            var tokens = await _dbContext.RefreshTokens
                .Where(rt => rt.UserId == userId && rt.Revoked == null && rt.Expires > now)
                .ToListAsync();
            foreach (var t in tokens) t.Revoked = now;
            if (tokens.Count > 0) await _dbContext.SaveChangesAsync();
        }

        public async Task<IdentityResult> ChangeEmailAsync(string userId, string newEmail, string currentPassword)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return IdentityResult.Failed(new IdentityError { Description = "User not found." });
            var pwCheck = await _signInManager.CheckPasswordSignInAsync(user, currentPassword, false);
            if (!pwCheck.Succeeded) return IdentityResult.Failed(new IdentityError { Description = "Current password is incorrect." });
            var existing = await _userManager.FindByEmailAsync(newEmail);
            if (existing != null && existing.Id != user.Id) return IdentityResult.Failed(new IdentityError { Description = "Email is already in use." });
            var token = await _userManager.GenerateChangeEmailTokenAsync(user, newEmail);
            try
            {
                var link = $"https://auth.findjob.nu/api/auth/confirm-change-email?userId={Uri.EscapeDataString(user.Id)}&newEmail={Uri.EscapeDataString(newEmail)}&token={Uri.EscapeDataString(token)}";
                await _emailSender.SendAsync(newEmail, "Confirm your email change", $"Confirm email change: <a href=\"{link}\">Click here</a>");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed sending email change confirmation");
                return IdentityResult.Failed(new IdentityError { Description = "Failed to send confirmation email." });
            }
            return IdentityResult.Success;
        }

        public async Task<IdentityResult> ConfirmChangeEmailAsync(string userId, string newEmail, string token)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return IdentityResult.Failed(new IdentityError { Description = "User not found." });
            var res = await _userManager.ChangeEmailAsync(user, newEmail, token);
            if (!res.Succeeded) return res;
            user.UserName = newEmail;
            user.NormalizedEmail = _userManager.NormalizeEmail(newEmail);
            user.NormalizedUserName = _userManager.NormalizeName(newEmail);
            await _userManager.UpdateAsync(user);
            await RevokeAllRefreshTokensAsync(user.Id);
            return IdentityResult.Success;
        }

        public async Task<IdentityResult> DisableAccountAsync(string userId, string currentPassword)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return IdentityResult.Failed(new IdentityError { Description = "User not found." });
            if (!user.IsLinkedInUser)
            {
                var pwCheck = await _signInManager.CheckPasswordSignInAsync(user, currentPassword, false);
                if (!pwCheck.Succeeded) return IdentityResult.Failed(new IdentityError { Description = "Current password is incorrect." });
            }
            user.LockoutEnabled = true;
            user.LockoutEnd = DateTimeOffset.UtcNow.AddYears(100);
            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded) return result;
            await RevokeAllRefreshTokensAsync(user.Id);
            return IdentityResult.Success;
        }
    }
}