using FindjobnuService.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SharedInfrastructure.Cities;
using SharedInfrastructure.Skills;


namespace FindjobnuService.Repositories.Context
{
    public class FindjobnuContext(DbContextOptions<FindjobnuContext> options) : DbContext(options)
    {
        public DbSet<Job> Jobs { get; set; }
        public DbSet<JobSnapshot> JobSnapshots { get; set; }
        public DbSet<JobImage> JobImages { get; set; }
        public DbSet<JobCategory> JobCategories { get; set; }
        public DbSet<JobIndexPosts> JobIndexPosts { get; set; }
        public DbSet<Category> Categories { get; set; }
        public DbSet<City> Cities { get; set; }
        public DbSet<CanonicalSkill> CanonicalSkills { get; set; }
        public DbSet<SkillSynonym> SkillSynonyms { get; set; }
        public DbSet<Profile> Profiles { get; set; }
        public DbSet<Experience> Experiences { get; set; }
        public DbSet<Education> Educations { get; set; }
        public DbSet<Interest> Interests { get; set; }
        public DbSet<Accomplishment> Accomplishments { get; set; }
        public DbSet<Contact> Contacts { get; set; }
        public DbSet<Skill> Skills { get; set; }
        public DbSet<JobKeyword> JobKeywords { get; set; }
        public DbSet<JobAgent> JobAgents { get; set; }
        public DbSet<NewsletterSubscription> NewsletterSubscriptions { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Job>(entity =>
            {
                entity.ToTable("jobs");
                entity.HasKey(j => j.JobId);
                entity.HasIndex(j => j.CanonicalJobUrl).IsUnique();
                entity.HasOne(j => j.CurrentSnapshot)
                    .WithMany()
                    .HasForeignKey(j => j.CurrentSnapshotId)
                    .OnDelete(DeleteBehavior.NoAction);
            });

            modelBuilder.Entity<JobSnapshot>(entity =>
            {
                entity.ToTable("job_snapshots");
                entity.HasKey(s => s.JobSnapshotId);
                entity.HasOne(s => s.Job)
                    .WithMany(j => j.Snapshots)
                    .HasForeignKey(s => s.JobId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(s => s.BannerImage)
                    .WithMany()
                    .HasForeignKey(s => s.BannerImageId)
                    .OnDelete(DeleteBehavior.NoAction);
                entity.HasOne(s => s.FooterImage)
                    .WithMany()
                    .HasForeignKey(s => s.FooterImageId)
                    .OnDelete(DeleteBehavior.NoAction);
            });

            modelBuilder.Entity<JobImage>(entity =>
            {
                entity.ToTable("job_images");
                entity.HasKey(i => i.JobImageId);
                entity.HasOne(i => i.Job)
                    .WithMany(j => j.Images)
                    .HasForeignKey(i => i.JobId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Category>(entity =>
            {
                entity.ToTable("categories");
                entity.HasKey(c => c.CategoryId);
                entity.HasIndex(c => c.CategoryKey).IsUnique();
            });

            modelBuilder.Entity<JobCategory>(entity =>
            {
                entity.ToTable("job_categories");
                entity.HasKey(jc => new { jc.JobId, jc.CategoryId });
                entity.HasOne(jc => jc.Job)
                    .WithMany(j => j.JobCategories)
                    .HasForeignKey(jc => jc.JobId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(jc => jc.Category)
                    .WithMany(c => c.JobCategories)
                    .HasForeignKey(jc => jc.CategoryId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<JobKeyword>(entity =>
            {
                entity.ToTable("job_keywords");
                entity.HasKey(k => k.JobKeywordId);
                entity.HasOne(k => k.JobSnapshot)
                    .WithMany(s => s.Keywords)
                    .HasForeignKey(k => k.JobSnapshotId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<JobIndexPosts>(entity =>
            {
                entity.ToTable("JobIndexPostingsExtended");
                entity.HasKey(j => j.JobID);
                entity.HasMany(j => j.Categories)
                    .WithMany()
                    .UsingEntity<Dictionary<string, object>>(
                        "LegacyJobIndexPostCategories",
                        right => right.HasOne<Category>().WithMany().HasForeignKey("CategoryId"),
                        left => left.HasOne<JobIndexPosts>().WithMany().HasForeignKey("JobId"),
                        join =>
                        {
                            join.HasKey("JobId", "CategoryId");
                            join.ToTable("LegacyJobIndexPostCategories");
                        });
            });

            modelBuilder.Entity<City>(entity =>
            {
                entity.ToTable("Cities");
                entity.HasKey(s => s.Id);
                entity.Property(s => s.Name).IsRequired().HasMaxLength(200);
                entity.Property(s => s.Slug).IsRequired().HasMaxLength(128);
                entity.Property(s => s.ExternalId).IsRequired();
                entity.HasIndex(s => s.Name);
                entity.HasIndex(s => s.Slug).IsUnique();
                entity.HasIndex(s => s.ExternalId).IsUnique();
            });

            modelBuilder.Entity<CanonicalSkill>(entity =>
            {
                entity.ToTable("CanonicalSkills");
                entity.HasKey(s => s.Id);
                entity.Property(s => s.Name).IsRequired().HasMaxLength(200);
                entity.Property(s => s.Slug).IsRequired().HasMaxLength(128);
                entity.Property(s => s.Category).HasMaxLength(100);
                entity.Property(s => s.ExternalId).IsRequired();
                entity.HasIndex(s => s.Name);
                entity.HasIndex(s => s.Slug).IsUnique();
                entity.HasIndex(s => s.ExternalId).IsUnique();
                entity.HasMany(s => s.Synonyms)
                    .WithOne(syn => syn.CanonicalSkill)
                    .HasForeignKey(syn => syn.CanonicalSkillId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<SkillSynonym>(entity =>
            {
                entity.ToTable("SkillSynonyms");
                entity.HasKey(s => s.Id);
                entity.Property(s => s.Synonym).IsRequired().HasMaxLength(200);
                entity.HasIndex(s => s.Synonym);
            });

            modelBuilder.Entity<Profile>().HasKey(p => p.Id);
            modelBuilder.Entity<Profile>()
                .HasIndex(p => p.UserId)
                .IsUnique();

            modelBuilder.Entity<NewsletterSubscription>(entity =>
            {
                entity.ToTable("NewsletterSubscriptions");
                entity.Property(n => n.Email).IsRequired().HasMaxLength(320);
                entity.HasIndex(n => n.Email).IsUnique();
            });

            var keywordsConverter = new ValueConverter<List<string>?, string?>(
                v => v == null ? null : string.Join(",", v),
                v => string.IsNullOrWhiteSpace(v) ? new List<string>() : v.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList()
            );
            var keywordsComparer = new ValueComparer<List<string>?>(
                (c1, c2) => (c1 == null && c2 == null) || (c1 != null && c2 != null && c1.SequenceEqual(c2)),
                c => c == null ? 0 : c.Aggregate(0, (a, v) => HashCode.Combine(a, v != null ? v.GetHashCode() : 0)),
                c => c == null ? null : c.ToList()
            );
            modelBuilder.Entity<Profile>()
                .Property(p => p.Keywords)
                .HasConversion(keywordsConverter)
                .Metadata.SetValueComparer(keywordsComparer);

            modelBuilder.Entity<JobAgent>()
                .Property(j => j.PreferredLocations)
                .HasConversion(keywordsConverter)
                .Metadata.SetValueComparer(keywordsComparer);

            modelBuilder.Entity<JobAgent>()
                .Property(j => j.IncludeKeywords)
                .HasConversion(keywordsConverter)
                .Metadata.SetValueComparer(keywordsComparer);

            modelBuilder.Entity<JobAgent>()
                .Property(j => j.PreferredCategoryKeys)
                .HasColumnName("PreferredCategoryIds")
                .HasConversion(keywordsConverter)
                .Metadata.SetValueComparer(keywordsComparer);

            // Profile relationships
            modelBuilder.Entity<Profile>()
                .OwnsOne(p => p.BasicInfo, b =>
                {
                    b.Property(bi => bi.FirstName).IsRequired().HasMaxLength(50);
                    b.Property(bi => bi.LastName).IsRequired().HasMaxLength(100);
                    b.Property(bi => bi.PhoneNumber).HasMaxLength(100);
                });
            modelBuilder.Entity<Profile>()
                .HasMany(p => p.Experiences)
                .WithOne(e => e.Profile)
                .HasForeignKey(e => e.ProfileId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<Profile>()
                .HasMany(p => p.Accomplishments)
                .WithOne(e => e.Profile)
                .HasForeignKey(e => e.ProfileId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<Profile>()
                .HasMany(p => p.Contacts)
                .WithOne(e => e.Profile)
                .HasForeignKey(e => e.ProfileId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<Profile>()
                .HasMany(p => p.Skills)
                .WithOne(e => e.Profile)
                .HasForeignKey(e => e.ProfileId)
                .OnDelete(DeleteBehavior.Cascade);

            // One-to-one: Profile <-> JobAgent
            modelBuilder.Entity<Profile>()
                .HasOne(p => p.JobAgent)
                .WithOne(ja => ja.Profile)
                .HasForeignKey<JobAgent>(ja => ja.ProfileId)
                .OnDelete(DeleteBehavior.Cascade);
        }

    }
}
