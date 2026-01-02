using FindjobnuService.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SharedInfrastructure.Cities;


namespace FindjobnuService.Repositories.Context
{
    public class FindjobnuContext(DbContextOptions<FindjobnuContext> options) : DbContext(options)
    {
        public DbSet<JobIndexPosts> JobIndexPosts { get; set; }
        public DbSet<Category> Categories { get; set; }
        public DbSet<City> Cities { get; set; }
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
            modelBuilder.Entity<JobIndexPosts>().ToTable("JobIndexPostingsExtended").HasKey(s => s.JobID);
            modelBuilder.Entity<JobIndexPosts>().HasIndex(s => s.JobUrl).IsUnique();

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
            var intListConverter = new ValueConverter<List<int>?, string?>(
                v => v == null ? null : string.Join(',', v),
                v => ConvertDelimitedStringToIntList(v)
            );
            var intListComparer = new ValueComparer<List<int>?>(
                (c1, c2) => (c1 == null && c2 == null) || (c1 != null && c2 != null && c1.SequenceEqual(c2)),
                c => c == null ? 0 : c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
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
                .Property(j => j.PreferredCategoryIds)
                .HasConversion(intListConverter)
                .Metadata.SetValueComparer(intListComparer);

            // Many-to-many: JobIndexPosts <-> Category
            modelBuilder.Entity<JobIndexPosts>()
                .HasMany(j => j.Categories)
                .WithMany(c => c.JobIndexPosts)
                .UsingEntity<Dictionary<string, object>>(
                    "JobCategories",
                    j => j.HasOne<Category>().WithMany().HasForeignKey("CategoryID").HasPrincipalKey("CategoryID").OnDelete(DeleteBehavior.Cascade),
                    c => c.HasOne<JobIndexPosts>().WithMany().HasForeignKey("JobID").HasPrincipalKey("JobID").OnDelete(DeleteBehavior.Cascade),
                    je =>
                    {
                        je.HasKey("JobID", "CategoryID");
                        je.ToTable("JobCategories");
                    }
                );

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

        private static List<int> ConvertDelimitedStringToIntList(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return new List<int>();
            }

            var results = new List<int>();
            foreach (var segment in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(segment.Trim(), out var parsed))
                {
                    results.Add(parsed);
                }
            }

            return results;
        }
    }
}
