using FindjobnuService.DTOs.Requests;
using FindjobnuService.MessageContracts;
using FindjobnuService.Models;
using FindjobnuService.Repositories.Context;
using FindjobnuService.Services;
using JobAgentWorkerService.Repositories;
using JobAgentWorkerService.Templates;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using SharedInfrastructure.Email;
using System.Collections.Generic;
using System.Linq;

namespace JobAgentWorkerService.Consumers
{
    public class JobAgentEmailRequestedConsumer : IConsumer<JobAgentEmailRequested>
    {
        private readonly ILogger<JobAgentEmailRequestedConsumer> _logger;
        private readonly FindjobnuContext _db;
        private readonly AuthDbContext _authDb;
        private readonly IJobIndexPostsService _jobs;
        private readonly IEmailSender _email;
        private readonly IConfiguration _config;

        public JobAgentEmailRequestedConsumer(
            ILogger<JobAgentEmailRequestedConsumer> logger,
            FindjobnuContext db,
            AuthDbContext authDb,
            IJobIndexPostsService jobs,
            IEmailSender email,
            IConfiguration config)
        {
            _logger = logger;
            _db = db;
            _authDb = authDb;
            _jobs = jobs;
            _email = email;
            _config = config;
        }

        public async Task Consume(ConsumeContext<JobAgentEmailRequested> context)
        {
            var msg = context.Message;
            _logger.LogInformation("Consuming JobAgentEmailRequested: ProfileId={ProfileId}, UserId={UserId}, Frequency={Frequency}", msg.ProfileId, msg.UserId, msg.Frequency);

            // Load profile and agent for token
            var profile = await _db.Profiles
                .Include(p => p.BasicInfo)
                .Include(p => p.JobAgent)
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == msg.ProfileId);
            if (profile == null)
            {
                _logger.LogWarning("Profile not found: {ProfileId}", msg.ProfileId);
                return;
            }

            var agent = profile.JobAgent;

            // Get user email from Auth database
            var user = await _authDb.AspNetUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == msg.UserId);
            var toEmail = user?.Email;
            if (string.IsNullOrWhiteSpace(toEmail))
            {
                _logger.LogWarning("No email found for UserId={UserId}; skipping email.", msg.UserId);
                return;
            }

            // Fetch top recommended jobs for this user; page 1
            var recommended = await _jobs.GetRecommendedJobsByUserAndProfile(msg.UserId, new RecommendedJobsRequest(null, null, null, null, null, 1, 20));
            var filteredItems = ApplyFilters(recommended.Items ?? Enumerable.Empty<JobIndexPosts>(), agent)
                .Take(10)
                .ToList();

            if (filteredItems.Count == 0)
            {
                _logger.LogInformation("No filtered job recommendations to send for ProfileId={ProfileId}", msg.ProfileId);
                return;
            }

            var firstName = profile.BasicInfo?.FirstName ?? user?.FirstName ?? "there";

            // Build unsubscribe link from PublicBaseUrl and agent token
            var token = profile.JobAgent?.UnsubscribeToken ?? string.Empty;
            var baseUrl = _config["PublicBaseUrl"]?.TrimEnd('/') ?? "https://findjob.nu";
            var unsubscribeLink = string.IsNullOrWhiteSpace(token) ? string.Empty : $"{baseUrl}/api/jobagent/unsubscribe/{token}";

            var filtersSummary = BuildFiltersSummary(agent);
            var html = EmailTemplates.JobRecommendationsHtml(firstName, msg.Frequency, filteredItems, unsubscribeLink, filtersSummary);
            var subject = $"Your {msg.Frequency} job recommendations";

            await _email.SendAsync(toEmail!, subject, html, context.CancellationToken);
            _logger.LogInformation("Sent job recommendations email to {Email} for ProfileId={ProfileId}", toEmail, msg.ProfileId);
        }

        private static IEnumerable<JobIndexPosts> ApplyFilters(IEnumerable<JobIndexPosts> jobs, JobAgent? agent)
        {
            if (agent == null) return jobs;

            return jobs.Where(job => MatchesLocation(job, agent) && MatchesCategories(job, agent) && MatchesKeywords(job, agent));
        }

        private static bool MatchesLocation(JobIndexPosts job, JobAgent agent)
        {
            if (agent.PreferredLocations == null || agent.PreferredLocations.Count == 0) return true;
            if (string.IsNullOrWhiteSpace(job.JobLocation)) return false;

            return agent.PreferredLocations.Any(loc =>
                !string.IsNullOrWhiteSpace(loc) &&
                job.JobLocation!.Contains(loc, StringComparison.OrdinalIgnoreCase));
        }

        private static bool MatchesCategories(JobIndexPosts job, JobAgent agent)
        {
            if (agent.PreferredCategoryIds == null || agent.PreferredCategoryIds.Count == 0) return true;
            if (job.Categories == null || job.Categories.Count == 0) return false;

            return job.Categories.Any(c => agent.PreferredCategoryIds.Contains(c.CategoryID));
        }

        private static bool MatchesKeywords(JobIndexPosts job, JobAgent agent)
        {
            if (agent.IncludeKeywords == null || agent.IncludeKeywords.Count == 0) return true;

            var searchable = string.Join(' ', new[] { job.JobTitle, job.JobDescription, job.CompanyName }) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(searchable)) return false;

            return agent.IncludeKeywords.Any(keyword =>
                !string.IsNullOrWhiteSpace(keyword) &&
                searchable.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        private static string? BuildFiltersSummary(JobAgent? agent)
        {
            if (agent == null) return null;

            var parts = new List<string>();
            if (agent.PreferredLocations != null && agent.PreferredLocations.Count > 0)
            {
                parts.Add($"Locations: {string.Join(", ", agent.PreferredLocations)}");
            }
            if (agent.PreferredCategoryIds != null && agent.PreferredCategoryIds.Count > 0)
            {
                parts.Add($"Categories: {string.Join(", ", agent.PreferredCategoryIds)}");
            }
            if (agent.IncludeKeywords != null && agent.IncludeKeywords.Count > 0)
            {
                parts.Add($"Keywords: {string.Join(", ", agent.IncludeKeywords)}");
            }

            return parts.Count == 0 ? null : string.Join(" | ", parts);
        }
    }
}
