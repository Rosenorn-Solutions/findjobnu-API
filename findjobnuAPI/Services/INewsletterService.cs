using FindjobnuService.DTOs.Responses;

namespace FindjobnuService.Services
{
    public interface INewsletterService
    {
        Task<NewsletterSubscribeResponse> SubscribeAsync(string email);
    }
}
