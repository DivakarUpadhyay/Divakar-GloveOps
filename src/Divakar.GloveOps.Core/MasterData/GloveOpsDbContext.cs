using Microsoft.EntityFrameworkCore;

namespace Divakar.GloveOps.Core.MasterData;

/// <summary>Base for master-data entities: every row carries a SQL ROWVERSION for optimistic concurrency.</summary>
public abstract class MasterEntity
{
    public byte[] RowVer { get; set; } = [];
}

public class PlantEntity
{
    public int PlantId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Country { get; set; } = "";
    public List<ProductEntity> Products { get; set; } = [];
}

public class CustomerEntity : MasterEntity
{
    public int CustomerId { get; set; }
    public string Name { get; set; } = "";
    public string Country { get; set; } = "";
}

public class ProductEntity : MasterEntity
{
    public int ProductId { get; set; }
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public string Material { get; set; } = "";
    public string Category { get; set; } = "";
    public int PlantId { get; set; }
    public PlantEntity? Plant { get; set; }
}

/// <summary>
/// EF Core for the master-data admin pages. The schema itself is owned by the SQL scripts
/// in /database (database-first), so this context maps existing tables and runs no migrations.
/// Sample requests and approvals stay on Dapper + stored procedures.
/// </summary>
public class GloveOpsDbContext(DbContextOptions<GloveOpsDbContext> options) : DbContext(options)
{
    public DbSet<CustomerEntity> Customers => Set<CustomerEntity>();
    public DbSet<ProductEntity> Products => Set<ProductEntity>();
    public DbSet<PlantEntity> Plants => Set<PlantEntity>();

    /// <summary>Read-only access to SampleRequest, only to check whether master data is in use.</summary>
    public IQueryable<SampleRequestRef> SampleRequests => Set<SampleRequestRef>().AsNoTracking();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<PlantEntity>(e =>
        {
            e.ToTable("Plant", "dbo").HasKey(x => x.PlantId);
            e.Property(x => x.Code).HasMaxLength(10).IsUnicode(false);
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Country).HasMaxLength(50);
        });

        b.Entity<CustomerEntity>(e =>
        {
            e.ToTable("Customer", "dbo").HasKey(x => x.CustomerId);
            e.Property(x => x.Name).HasMaxLength(150).IsRequired();
            e.Property(x => x.Country).HasMaxLength(50).IsRequired();
            e.Property(x => x.RowVer).IsRowVersion();
        });

        b.Entity<ProductEntity>(e =>
        {
            e.ToTable("Product", "dbo").HasKey(x => x.ProductId);
            e.Property(x => x.Sku).HasMaxLength(30).IsUnicode(false).IsRequired();
            e.HasIndex(x => x.Sku).IsUnique();
            e.Property(x => x.Name).HasMaxLength(150).IsRequired();
            e.Property(x => x.Material).HasMaxLength(10).IsUnicode(false).IsRequired();
            e.Property(x => x.Category).HasMaxLength(50).IsRequired();
            e.Property(x => x.RowVer).IsRowVersion();
            e.HasOne(x => x.Plant).WithMany(p => p.Products).HasForeignKey(x => x.PlantId);
        });

        b.Entity<SampleRequestRef>(e =>
        {
            e.ToTable("SampleRequest", "dbo").HasKey(x => x.RequestId);
        });
    }
}

/// <summary>Minimal read-only mapping of dbo.SampleRequest (keys only).</summary>
public class SampleRequestRef
{
    public int RequestId { get; set; }
    public int CustomerId { get; set; }
    public int ProductId { get; set; }
}
