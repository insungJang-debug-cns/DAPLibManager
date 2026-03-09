using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

internal sealed class TrackConfiguration : IEntityTypeConfiguration<Track>
{
    public void Configure(EntityTypeBuilder<Track> builder)
    {
        builder.ToTable("Tracks");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id)
               .HasConversion<string>();

        builder.Property(t => t.Title)
               .IsRequired();

        builder.Property(t => t.FilePath)
               .IsRequired();

        builder.Property(t => t.Duration)
               .HasConversion(
                   v => v.Ticks,
                   v => TimeSpan.FromTicks(v));

        builder.Property(t => t.FileSize)
               .HasDefaultValue(0L);

        builder.Property(t => t.LastModifiedUtc)
               .HasConversion(
                   v => v.ToString("O"),
                   v => DateTime.Parse(v, null, System.Globalization.DateTimeStyles.RoundtripKind))
               .HasDefaultValue(DateTime.MinValue);

        // Indexes for fast search (~10,000 tracks)
        builder.HasIndex(t => t.Title);
        builder.HasIndex(t => t.ArtistName);
        builder.HasIndex(t => t.AlbumTitle);
        builder.HasIndex(t => new { t.ArtistName, t.AlbumTitle });
        builder.HasIndex(t => t.FilePath).IsUnique();
    }
}
