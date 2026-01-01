using FindjobnuService.Repositories.Context;
using FindjobnuService.Services;
using JobAgentWorkerService.Consumers;
using JobAgentWorkerService.Repositories;
using JobAgentWorkerService.Services;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Serilog;
using SharedInfrastructure.Email;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddJsonFile("appsettings.json", optional: true)
                     .AddJsonFile("appsettings.Development.json", optional: true)
                     .AddEnvironmentVariables();

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .WriteTo.Console()
    .CreateLogger();

// DbContext for accessing Profiles/JobAgents and jobs
var connectionString = builder.Configuration.GetConnectionString("FindjobnuConnection");
if (!string.IsNullOrWhiteSpace(connectionString))
{
    builder.Services.AddDbContext<FindjobnuContext>(options => options.UseSqlServer(connectionString));
}
else
{
    Log.Logger.Warning("No FindjobnuConnection connection string found for JobAgentWorker. Scheduler will not run DB queries.");
}

// Auth DB for getting user emails
var authConnection = builder.Configuration.GetConnectionString("FindjobnuAuthConnection");
if (!string.IsNullOrWhiteSpace(authConnection))
{
    builder.Services.AddDbContext<AuthDbContext>(options => options.UseSqlServer(authConnection));
}
else
{
    Log.Logger.Warning("No FindjobnuAuthConnection connection string found; emails may not be deliverable.");
}

// Domain services reused from API
builder.Services.AddScoped<IJobIndexPostsService, JobIndexPostsService>(sp =>
{
    var db = sp.GetRequiredService<FindjobnuContext>();
    var logger = sp.GetRequiredService<ILogger<JobIndexPostsService>>();
    return new JobIndexPostsService(db, logger);
});

// Email sender
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();

builder.Services.AddMassTransit(x =>
{
    x.SetKebabCaseEndpointNameFormatter();

    x.AddConsumer<JobAgentEmailRequestedConsumer>(cfg =>
    {
        cfg.UseMessageRetry(r => r.Exponential(5, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(5)));
    });

    x.UsingRabbitMq((context, cfg) =>
    {
        var host = builder.Configuration.GetValue<string>("RabbitMQ:Host") ?? "localhost";
        var username = builder.Configuration.GetValue<string>("RabbitMQ:Username") ?? "guest";
        var password = builder.Configuration.GetValue<string>("RabbitMQ:Password") ?? "guest";

        cfg.Host(host, "/", h =>
        {
            h.Username(username);
            h.Password(password);
        });

        cfg.UseMessageRetry(r => r.Exponential(5, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(5)));

        cfg.ReceiveEndpoint("job-agent-email-requested", e =>
        {
            e.ConfigureConsumer<JobAgentEmailRequestedConsumer>(context);
        });
    });
});

// Scheduler options
builder.Services.Configure<JobAgentSchedulerOptions>(builder.Configuration.GetSection("JobAgentScheduler"));

// Add scheduler background service
builder.Services.AddHostedService<JobAgentScheduler>();

var host = builder.Build();

await host.RunAsync();
