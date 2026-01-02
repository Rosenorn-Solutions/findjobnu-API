using FindjobnuService.DTOs.Responses;
using FindjobnuService.Models;
using FindjobnuService.Repositories.Context;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace FindjobnuService.Services
{
    public class NewsletterService : INewsletterService
    {
        private readonly FindjobnuContext _db;
        private readonly ILogger<NewsletterService> _logger;
        private readonly EmailAddressAttribute _emailValidator = new();

        public NewsletterService(FindjobnuContext db, ILogger<NewsletterService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<NewsletterSubscribeResponse> SubscribeAsync(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                throw new ArgumentException("Email is required", nameof(email));

            var normalizedEmail = email.Trim().ToLowerInvariant();
            if (!_emailValidator.IsValid(normalizedEmail))
                throw new ArgumentException("Invalid email format", nameof(email));

            var exists = await _db.NewsletterSubscriptions
                .AsNoTracking()
                .AnyAsync(n => n.Email.ToLower() == normalizedEmail);
            if (exists)
            {
                return new NewsletterSubscribeResponse(true, true);
            }

            try
            {
                var entity = new NewsletterSubscription { Email = normalizedEmail, CreatedAt = DateTime.UtcNow };
                _db.NewsletterSubscriptions.Add(entity);
                await _db.SaveChangesAsync();
                return new NewsletterSubscribeResponse(true, false);
            }
            catch (DbUpdateException ex)
            {
                _logger.LogWarning(ex, "Failed to insert newsletter subscription for {Email}", normalizedEmail);
                var stillExists = await _db.NewsletterSubscriptions.AsNoTracking().AnyAsync(n => n.Email.ToLower() == normalizedEmail);
                return new NewsletterSubscribeResponse(stillExists, stillExists);
            }
        }
    }
}
