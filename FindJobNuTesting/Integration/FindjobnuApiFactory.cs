using FindjobnuService.Repositories.Context;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FindjobnuTesting.Integration
{
    public class FindjobnuApiFactory : WebApplicationFactory<FindjobnuService.Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Force Testing environment early so Program uses the in-memory database
            builder.UseEnvironment("Testing");

            builder.ConfigureAppConfiguration((context, cfg) =>
            {
                cfg.AddEnvironmentVariables();
                var overrides = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    {"Serilog:Using:0", "Serilog.Sinks.Console"},
                    {"Serilog:WriteTo:0:Name", "Console"},
                    {"Serilog:MinimumLevel:Default", "Information"},
                    {"ConnectionStrings:FindjobnuConnection", string.Empty}
                };
                cfg.AddInMemoryCollection(overrides);
            });

            builder.ConfigureServices(services =>
            {
                // Remove existing DbContext registrations for FindjobnuContext
                var descriptorsToRemove = services
                    .Where(d => d.ServiceType == typeof(DbContextOptions<FindjobnuContext>) ||
                                d.ServiceType == typeof(FindjobnuContext) ||
                                d.ServiceType == typeof(IDbContextFactory<FindjobnuContext>))
                    .ToList();
                foreach (var descriptor in descriptorsToRemove)
                {
                    services.Remove(descriptor);
                }

                // Register DbContextFactory for in-memory tests
                services.AddDbContextFactory<FindjobnuContext>(options =>
                {
                    options.UseInMemoryDatabase("IntegrationTestsDb");
                });

                // Register scoped DbContext from the factory
                services.AddScoped(sp =>
                    sp.GetRequiredService<IDbContextFactory<FindjobnuContext>>().CreateDbContext());
            });
        }
    }
}
