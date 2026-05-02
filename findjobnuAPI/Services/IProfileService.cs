using FindjobnuService.DTOs;
using FindjobnuService.Models;

namespace FindjobnuService.Services
{
    public interface IProfileService
    {
        Task<ProfileDto?> GetByUserIdAsync(string userId);
        Task<Profile?> CreateAsync(Profile profile);
        Task<bool> UpdateAsync(int id, Profile profile, string authenticatedUserId);
        Task<PagedList<JobIndexPosts>> GetSavedJobsByUserIdAsync(string userId, int page = 1);
        Task<bool> SaveJobAsync(string userId, string jobReference);
        Task<bool> RemoveSavedJobAsync(string userId, string jobReference);
        Task<BasicInfoDto?> GetProfileBasicInfoByUserIdAsync(string userId);
        Task<List<ExperienceDto>> GetProfileExperienceByUserIdAsync(string userId);
        Task<List<SkillDto>> GetProfileSkillsByUserIdAsync(string userId);
        Task<List<EducationDto>> GetProfileEducationByUserIdAsync(string userId);
    }
}
