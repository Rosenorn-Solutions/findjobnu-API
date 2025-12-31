using AuthService.Data;
using AuthService.Endpoints;
using AuthService.Entities;
using AuthService.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using System.Text;
using SharedInfrastructure.Cities;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SharedInfrastructure.Health;
using Prometheus;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

var loggerConfig = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console();

Log.Logger = loggerConfig.CreateLogger();
builder.Host.UseSerilog();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("FindjobnuConnection")));

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequiredLength = 8;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.User.RequireUniqueEmail = true;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

// Register application services
builder.Services.AddScoped<IAuthService, AuthService.Services.AuthService>();

var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var secretKey = jwtSettings["SecretKey"];
var issuer = jwtSettings["Issuer"];
var audience = jwtSettings["Audience"];
if (string.IsNullOrEmpty(secretKey) || string.IsNullOrEmpty(issuer) || string.IsNullOrEmpty(audience))
{
    throw new InvalidOperationException("JWT settings are not properly configured");
}

// Ensure JWT bearer is the default scheme for authenticate/challenge
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = issuer,
        ValidAudience = audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey))
    };
});

// Register Swagger services for minimal APIs/endpoints
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// CORS: read allowed origins from configuration
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();

//bool IsOriginAllowed(string origin)
//{
//    if (!Uri.TryCreate(origin, UriKind.Absolute, out var o)) return false;

//    foreach (var pattern in allowedOrigins)
//    {
//        if (string.IsNullOrWhiteSpace(pattern)) continue;

//        // Wildcard subdomain support like https://*.findjob.nu
//        if (pattern.Contains("*"))
//        {
//            if (pattern.StartsWith("https://*.", StringComparison.OrdinalIgnoreCase))
//            {
//                var domain = pattern.Substring("https://*.".Length);
//                if (string.Equals(o.Scheme, "https", StringComparison.OrdinalIgnoreCase) &&
//                    (string.Equals(o.Host, domain, StringComparison.OrdinalIgnoreCase) ||
//                     o.Host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase)))
//                {
//                    return true;
//                }
//            }
//            continue;
//        }

//        if (Uri.TryCreate(pattern, UriKind.Absolute, out var p))
//        {
//            var schemeOk = string.Equals(o.Scheme, p.Scheme, StringComparison.OrdinalIgnoreCase);
//            var hostOk = string.Equals(o.Host, p.Host, StringComparison.OrdinalIgnoreCase);
//            var portOk = p.IsDefaultPort || p.Port == -1 || p.Port == o.Port; // allow any port if not specified
//            if (schemeOk && hostOk && portOk)
//            {
//                return true;
//            }
//        }
//    }

//    return false;
//}

builder.Services.AddCors(options =>
{
    options.AddPolicy("ConfiguredCors", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// Register Swagger services for minimal APIs/endpoints
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddAuthorization();
builder.Services.AddHttpClient();

builder.Services.AddSingleton(new ApplicationHealthMetadata(DateTimeOffset.UtcNow));

builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: new[] { "live" })
    .AddDbContextCheck<ApplicationDbContext>("database", tags: new[] { "ready" });

var app = builder.Build();

await app.Services.SeedCitiesAsync<ApplicationDbContext>();

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost
});

// Use CORS before auth/endpoints
app.UseCors("ConfiguredCors");

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "AuthService API v1");
});

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseHttpsRedirection();
}

app.UseHttpMetrics();

app.MapHealthChecks("/healthz/live", new HealthCheckOptions
{
    Predicate = r => r.Tags.Contains("live"),
    ResponseWriter = HealthCheckResponseWriter.WriteDetailedResponse
});

app.MapHealthChecks("/healthz/ready", new HealthCheckOptions
{
    Predicate = r => r.Tags.Contains("ready"),
    ResponseWriter = HealthCheckResponseWriter.WriteDetailedResponse
});

app.MapHealthChecks("/healthz", new HealthCheckOptions
{
    ResponseWriter = HealthCheckResponseWriter.WriteDetailedResponse
});

app.MapMetrics("/metrics");

app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapLinkedInAuthEndpoints();

await app.RunAsync();