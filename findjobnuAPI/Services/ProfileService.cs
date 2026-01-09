using FindjobnuService.DTOs;
using FindjobnuService.Mappers;
using FindjobnuService.Models;
using FindjobnuService.Repositories.Context;
using Microsoft.EntityFrameworkCore;

namespace FindjobnuService.Services
{
    public class ProfileService(FindjobnuContext db) : IProfileService
    {
        private readonly FindjobnuContext _db = db;

        public async Task<ProfileDto?> GetByUserIdAsync(string userId)
        {
            var profile = await _db.Profiles
                .Include(p => p.BasicInfo)
                .Include(p => p.Experiences)
                .Include(p => p.Educations)
                .Include(p => p.Interests)
                .Include(p => p.Accomplishments)
                .Include(p => p.Contacts)
                .Include(p => p.Skills)
                .Include(p => p.JobAgent)
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.UserId == userId);
            if (profile == null) return null;
            if (profile.BasicInfo == null) profile.BasicInfo = new BasicInfo();

            // Fetch categories for JobAgent if it exists and has preferred category IDs
            IEnumerable<Category>? categories = null;
            if (profile.JobAgent?.PreferredCategoryIds != null && profile.JobAgent.PreferredCategoryIds.Any())
            {
                categories = await _db.Categories
                    .Where(c => profile.JobAgent.PreferredCategoryIds.Contains(c.CategoryID))
                    .AsNoTracking()
                    .ToListAsync();
            }

            return new ProfileDto
            {
                Id = profile.Id,
                UserId = profile.UserId,
                LastUpdatedAt = profile.LastUpdatedAt,
                CreatedAt = profile.CreatedAt,
                SavedJobPosts = profile.SavedJobPosts,
                Keywords = profile.Keywords,
                HasJobAgent = profile.HasJobAgent,
                JobAgent = JobAgentMapper.ToDto(profile.JobAgent, categories),
                BasicInfo = new BasicInfoDto
                {
                    FirstName = profile.BasicInfo.FirstName,
                    LastName = profile.BasicInfo.LastName,
                    DateOfBirth = profile.BasicInfo.DateOfBirth,
                    PhoneNumber = profile.BasicInfo.PhoneNumber,
                    About = profile.BasicInfo.About,
                    Location = profile.BasicInfo.Location,
                    Company = profile.BasicInfo.Company,
                    JobTitle = profile.BasicInfo.JobTitle,
                    LinkedinUrl = profile.BasicInfo.LinkedinUrl,
                    OpenToWork = profile.BasicInfo.OpenToWork
                },
                Experiences = profile.Experiences?.Select(e => new ExperienceDto
                {
                    Id = e.Id,
                    PositionTitle = e.PositionTitle,
                    Company = e.Company,
                    FromDate = e.FromDate,
                    ToDate = e.ToDate,
                    Duration = e.Duration,
                    Location = e.Location,
                    Description = e.Description,
                    LinkedinUrl = e.LinkedinUrl
                }).ToList(),
                Educations = profile.Educations?.Select(e => new EducationDto
                {
                    Id = e.Id,
                    Institution = e.Institution,
                    Degree = e.Degree,
                    FromDate = e.FromDate,
                    ToDate = e.ToDate,
                    Description = e.Description,
                    LinkedinUrl = e.LinkedinUrl
                }).ToList(),
                Interests = profile.Interests?.Select(i => new InterestDto
                {
                    Id = i.Id,
                    Title = i.Title
                }).ToList(),
                Accomplishments = profile.Accomplishments?.Select(a => new AccomplishmentDto
                {
                    Id = a.Id,
                    Category = a.Category,
                    Title = a.Title
                }).ToList(),
                Contacts = profile.Contacts?.Select(c => new ContactDto
                {
                    Id = c.Id,
                    Name = c.Name,
                    Occupation = c.Occupation,
                    Url = c.Url
                }).ToList(),
                Skills = profile.Skills?.Select(s => new SkillDto
                {
                    Id = s.Id,
                    Name = s.Name,
                    Proficiency = (int)s.Proficiency
                }).ToList()
            };
        }

        public async Task<Profile?> CreateAsync(Profile profile)
        {
            // Ensure non-nullable DB columns are set explicitly
            profile.HasJobAgent = profile.JobAgent?.Enabled ?? false;
            profile.BasicInfo ??= new BasicInfo();

            _db.Profiles.Add(profile);
            await _db.SaveChangesAsync();
            return profile;
        }

        public async Task<bool> UpdateAsync(int id, Profile profile, string authenticatedUserId)
        {
            var entity = await _db.Profiles
                .Include(p => p.BasicInfo)
                .Include(p => p.Experiences)
                .Include(p => p.Educations)
                .Include(p => p.Interests)
                .Include(p => p.Accomplishments)
                .Include(p => p.Contacts)
                .Include(p => p.Skills)
                .FirstOrDefaultAsync(model => model.Id == id && model.UserId == authenticatedUserId);
            if (entity == null)
                return false;

            // Basic info
            entity.BasicInfo.FirstName = profile.BasicInfo.FirstName;
            entity.BasicInfo.LastName = profile.BasicInfo.LastName;
            entity.BasicInfo.DateOfBirth = profile.BasicInfo.DateOfBirth;
            entity.BasicInfo.PhoneNumber = profile.BasicInfo.PhoneNumber;
            entity.BasicInfo.About = profile.BasicInfo.About;
            entity.BasicInfo.Location = profile.BasicInfo.Location;
            entity.BasicInfo.Company = profile.BasicInfo.Company;
            entity.BasicInfo.JobTitle = profile.BasicInfo.JobTitle;
            entity.BasicInfo.LinkedinUrl = profile.BasicInfo.LinkedinUrl;
            entity.BasicInfo.OpenToWork = profile.BasicInfo.OpenToWork;

            // Simple fields
            entity.LastUpdatedAt = DateTime.UtcNow;
            entity.Keywords = profile.Keywords;
            entity.SavedJobPosts = profile.SavedJobPosts;

            // Collections
            _db.Experiences.RemoveRange(entity.Experiences ?? []);
            if (profile.Experiences != null)
            {
                foreach (var exp in profile.Experiences)
                {
                    exp.ProfileId = entity.Id;
                    _db.Experiences.Add(exp);
                }
            }

            _db.Educations.RemoveRange(entity.Educations ?? []);
            if (profile.Educations != null)
            {
                foreach (var edu in profile.Educations)
                {
                    edu.ProfileId = entity.Id;
                    _db.Educations.Add(edu);
                }
            }

            _db.Interests.RemoveRange(entity.Interests ?? []);
            if (profile.Interests != null)
            {
                foreach (var interest in profile.Interests)
                {
                    interest.ProfileId = entity.Id;
                    _db.Interests.Add(interest);
                }
            }

            _db.Accomplishments.RemoveRange(entity.Accomplishments ?? []);
            if (profile.Accomplishments != null)
            {
                foreach (var acc in profile.Accomplishments)
                {
                    acc.ProfileId = entity.Id;
                    _db.Accomplishments.Add(acc);
                }
            }

            _db.Contacts.RemoveRange(entity.Contacts ?? []);
            if (profile.Contacts != null)
            {
                foreach (var contact in profile.Contacts)
                {
                    contact.ProfileId = entity.Id;
                    _db.Contacts.Add(contact);
                }
            }

            _db.Skills.RemoveRange(entity.Skills ?? []);
            if (profile.Skills != null)
            {
                foreach (var skill in profile.Skills)
                {
                    skill.ProfileId = entity.Id;
                    _db.Skills.Add(skill);
                }
            }

            await _db.SaveChangesAsync();
            return true;
        }

        public async Task<PagedList<JobIndexPosts>> GetSavedJobsByUserIdAsync(string userId, int page = 1)
        {
            int pagesize = 20;

            var profile = await _db.Profiles.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId);
            if (profile == null || profile.SavedJobPosts == null || profile.SavedJobPosts.Count == 0)
                return new PagedList<JobIndexPosts>(0, pagesize, page, []);

            var jobIds = profile.SavedJobPosts
                .Select(id => int.TryParse(id, out var jid) ? jid : (int?)null)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToHashSet();

            if (jobIds.Count == 0)
                return new PagedList<JobIndexPosts>(0, pagesize, page, []);

            var jobs = await _db.JobIndexPosts
                .Where(j => jobIds.Contains(j.JobID))
                .AsNoTracking()
                .ToListAsync();
            return new PagedList<JobIndexPosts>(jobs.Count, pagesize, page, jobs);
        }

        public async Task<bool> SaveJobAsync(string userId, string jobId)
        {
            var profile = await _db.Profiles
                .FirstOrDefaultAsync(x => x.UserId == userId);
            if (profile == null)
            {
                return false;
            }
            if (profile.SavedJobPosts == null)
            {
                profile.SavedJobPosts = [];
            }
            if (!profile.SavedJobPosts.Contains(jobId))
            {
                profile.SavedJobPosts.Add(jobId);
                await _db.SaveChangesAsync();
                return true;
            }
            return false;
        }

        public async Task<bool> RemoveSavedJobAsync(string userId, string jobId)
        {
            var profile = await _db.Profiles
                .FirstOrDefaultAsync(x => x.UserId == userId);

            if (profile == null || profile.SavedJobPosts == null) return false;

            if (profile.SavedJobPosts.Remove(jobId))
            {
                await _db.SaveChangesAsync();
                return true;
            }
            return false;
        }

        private async Task<TDto?> GetProfileSingleDataByUserIdAsync<TProperty, TDto>(
            string userId,
            System.Linq.Expressions.Expression<Func<Profile, TProperty>> includeExpression,
            Func<Profile, TProperty?> selector,
            Func<TProperty, TDto> mapFunc)
            where TProperty : class
        {
            var profile = await _db.Profiles
                .Include(includeExpression)
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.UserId == userId);
            var property = profile == null ? null : selector(profile);
            if (property == null)
                return default;
            return mapFunc(property);
        }

        private async Task<List<TDto>> GetProfileCollectionDataByUserIdAsync<TProperty, TDto>(
            string userId,
            System.Linq.Expressions.Expression<Func<Profile, IEnumerable<TProperty>>> includeExpression,
            Func<Profile, IEnumerable<TProperty>?> selector,
            Func<TProperty, TDto> mapFunc)
            where TProperty : class
        {
            var profile = await _db.Profiles
                .Include(includeExpression)
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.UserId == userId);
            var collection = profile == null ? null : selector(profile);
            if (collection == null)
                return [];
            return collection.Select(mapFunc).ToList();
        }

        public Task<BasicInfoDto?> GetProfileBasicInfoByUserIdAsync(string userId)
        {
            return GetProfileSingleDataByUserIdAsync(
                userId,
                p => p.BasicInfo,
                p => p.BasicInfo,
                b => new BasicInfoDto
                {
                    FirstName = b.FirstName,
                    LastName = b.LastName,
                    DateOfBirth = b.DateOfBirth,
                    PhoneNumber = b.PhoneNumber,
                    About = b.About,
                    Location = b.Location,
                    Company = b.Company,
                    JobTitle = b.JobTitle,
                    LinkedinUrl = b.LinkedinUrl,
                    OpenToWork = b.OpenToWork
                });
        }

        public Task<List<ExperienceDto>> GetProfileExperienceByUserIdAsync(string userId)
        {
            return GetProfileCollectionDataByUserIdAsync(
                userId,
                p => p.Experiences ?? Enumerable.Empty<Experience>(),
                p => p.Experiences,
                e => new ExperienceDto
                {
                    Id = e.Id,
                    PositionTitle = e.PositionTitle,
                    Company = e.Company,
                    FromDate = e.FromDate,
                    ToDate = e.ToDate,
                    Duration = e.Duration,
                    Location = e.Location,
                    Description = e.Description,
                    LinkedinUrl = e.LinkedinUrl
                });
        }

        public Task<List<SkillDto>> GetProfileSkillsByUserIdAsync(string userId)
        {
            return GetProfileCollectionDataByUserIdAsync(
                userId,
                p => p.Skills ?? Enumerable.Empty<Skill>(),
                p => p.Skills,
                s => new SkillDto
                {
                    Id = s.Id,
                    Name = s.Name,
                    Proficiency = (int)s.Proficiency
                });
        }

        public Task<List<EducationDto>> GetProfileEducationByUserIdAsync(string userId)
        {
            return GetProfileCollectionDataByUserIdAsync(
                userId,
                p => p.Educations ?? Enumerable.Empty<Education>(),
                p => p.Educations,
                e => new EducationDto
                {
                    Id = e.Id,
                    Institution = e.Institution,
                    Degree = e.Degree,
                    FromDate = e.FromDate,
                    ToDate = e.ToDate,
                    Description = e.Description,
                    LinkedinUrl = e.LinkedinUrl
                });
        }
    }
}
