using System.ComponentModel.DataAnnotations;

namespace FindjobnuService.DTOs.Requests;

public class NewsletterSubscribeRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
}
