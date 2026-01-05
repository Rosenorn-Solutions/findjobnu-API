using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SharedInfrastructure.Skills;

public static class SkillSeederServiceProviderExtensions
{
    public static async Task SeedSkillsAsync<TContext>(this IServiceProvider services, CancellationToken cancellationToken = default)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();
        var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("SkillSeeder");

        await SkillCsvSeeder.SeedAsync(context, logger, cancellationToken);
    }
}
