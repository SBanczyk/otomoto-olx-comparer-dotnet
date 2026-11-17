using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarComparer.Web.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<IdentityUser>(options)
{
    public DbSet<Listing> Listings => Set<Listing>();
    public DbSet<ListingSnapshot> Snapshots => Set<ListingSnapshot>();
    public DbSet<Comparison> Comparisons => Set<Comparison>();
    public DbSet<ComparisonItem> ComparisonItems => Set<ComparisonItem>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}

internal sealed class ListingConfiguration : IEntityTypeConfiguration<Listing>
{
    public void Configure(EntityTypeBuilder<Listing> b)
    {
        b.Property(x => x.ExternalId).HasMaxLength(64);
        b.Property(x => x.Url).HasMaxLength(2048);
        b.Property(x => x.LastFetchMessage).HasMaxLength(500);
        b.Property(x => x.Portal).HasConversion<byte>();
        b.Property(x => x.LastFetchFailure).HasConversion<byte?>();
        b.Property(x => x.RowVersion).IsRowVersion();
        // Identity of a listing is its portal ID; a unique index on the 2048-char URL would exceed SQL Server's key size limit.
        b.HasIndex(x => new { x.Portal, x.ExternalId }).IsUnique();
    }
}

internal sealed class ListingSnapshotConfiguration : IEntityTypeConfiguration<ListingSnapshot>
{
    public void Configure(EntityTypeBuilder<ListingSnapshot> b)
    {
        b.Property(x => x.Brand).HasMaxLength(100);
        b.Property(x => x.Model).HasMaxLength(200);
        b.Property(x => x.FuelType).HasConversion<byte>();
        b.Property(x => x.Gearbox).HasConversion<byte>();
        b.Property(x => x.BodyType).HasConversion<byte?>();
        b.Property(x => x.Drive).HasConversion<byte?>();
        foreach (var raw in new[] { nameof(ListingSnapshot.FuelTypeRaw), nameof(ListingSnapshot.GearboxRaw), nameof(ListingSnapshot.BodyTypeRaw), nameof(ListingSnapshot.DriveRaw), nameof(ListingSnapshot.CountryOfOrigin) })
        {
            b.Property(raw).HasMaxLength(100);
        }

        b.Property(x => x.Price).HasPrecision(12, 2);
        b.Property(x => x.Currency).HasColumnType("char(3)");
        b.Property(x => x.PricePln).HasPrecision(12, 2);
        b.Property(x => x.ExchangeRate).HasPrecision(10, 6);
        b.Property(x => x.ExchangeRateDate).HasColumnType("date");
        b.Property(x => x.RawPayload).HasColumnType("nvarchar(max)");
        b.HasOne(x => x.Listing).WithMany(x => x.Snapshots).HasForeignKey(x => x.ListingId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.ListingId, x.FetchedAt }).IsDescending(false, true);
    }
}

internal sealed class ComparisonConfiguration : IEntityTypeConfiguration<Comparison>
{
    public void Configure(EntityTypeBuilder<Comparison> b)
    {
        b.Property(x => x.Id).HasColumnType("varchar(12)");
        b.Property(x => x.OwnerId).HasMaxLength(450);
        b.HasOne<IdentityUser>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.OwnerId, x.CreatedAt });
    }
}

internal sealed class ComparisonItemConfiguration : IEntityTypeConfiguration<ComparisonItem>
{
    public void Configure(EntityTypeBuilder<ComparisonItem> b)
    {
        b.HasKey(x => new { x.ComparisonId, x.Position });
        b.Property(x => x.ComparisonId).HasColumnType("varchar(12)");
        b.ToTable(t => t.HasCheckConstraint("CK_ComparisonItem_Position", "[Position] BETWEEN 1 AND 4"));
        b.HasOne(x => x.Comparison).WithMany(x => x.Items).HasForeignKey(x => x.ComparisonId).OnDelete(DeleteBehavior.Cascade);
        // No cascade from Listing: orphaned listings are removed by the cleanup job, and a listing in use must not vanish.
        b.HasOne(x => x.Listing).WithMany().HasForeignKey(x => x.ListingId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.ComparisonId, x.ListingId }).IsUnique();
    }
}
