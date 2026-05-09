using FlashInterview.Infrastructure.SensitiveWords;
using Microsoft.EntityFrameworkCore;

namespace FlashInterview.Infrastructure;

public sealed class FlashInterviewDbContext(DbContextOptions<FlashInterviewDbContext> options) : DbContext(options)
{
    public DbSet<SensitiveWordEntity> SensitiveWords => Set<SensitiveWordEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SensitiveWordEntity>(entity =>
        {
            entity.ToTable("SensitiveWords");
            entity.HasKey(word => word.Id);
            entity.Property(word => word.Value).HasMaxLength(256).IsRequired();
            entity.Property(word => word.NormalizedValue).HasMaxLength(256).IsRequired();
            entity.Property(word => word.Category).HasMaxLength(64);
            entity.Property(word => word.CreatedAt).IsRequired();
            entity.Property(word => word.UpdatedAt).IsRequired();
            entity.HasIndex(word => word.NormalizedValue).IsUnique();
            entity.HasIndex(word => new { word.Category, word.IsActive });
        });
    }
}
