using Divakar.GloveOps.Core.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Divakar.GloveOps.Core.MasterData;

/// <summary>One page of results plus the numbers a pager needs.</summary>
public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
}

public static class QueryableExtensions
{
    /// <summary>Generic paging for any EF Core query: one COUNT and one OFFSET/FETCH query.</summary>
    public static async Task<PagedResult<T>> ToPagedAsync<T>(this IQueryable<T> query, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<T>(items, page, pageSize, total);
    }
}

public record CustomerRow(int CustomerId, string Name, string Country, int RequestCount);
public record ProductRow(int ProductId, string Sku, string Name, string Material, string Category, string PlantName, int RequestCount);

public static class Materials
{
    public static readonly string[] All = ["NBR", "PVC", "TPE", "LATEX"];
}

/// <summary>
/// Master-data CRUD with EF Core: LINQ projections with AsNoTracking for lists,
/// change tracking for edits, ROWVERSION concurrency, and safe deletes.
/// </summary>
public sealed class MasterDataService(GloveOpsDbContext db)
{
    public const int PageSize = 10;

    // ---------- customers ----------

    public Task<PagedResult<CustomerRow>> CustomersAsync(string? search, int page)
    {
        var q = db.Customers.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(c => c.Name.Contains(s) || c.Country.Contains(s));
        }

        return q.OrderBy(c => c.Name)
            .Select(c => new CustomerRow(c.CustomerId, c.Name, c.Country,
                db.SampleRequests.Count(r => r.CustomerId == c.CustomerId)))
            .ToPagedAsync(page, PageSize);
    }

    public Task<CustomerEntity?> CustomerAsync(int id) =>
        db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.CustomerId == id);

    public async Task<int> SaveCustomerAsync(CustomerEntity input)
    {
        Validate(input);
        if (input.CustomerId == 0)
        {
            var entity = new CustomerEntity { Name = input.Name.Trim(), Country = input.Country.Trim() };
            db.Customers.Add(entity);
            await SaveAsync();
            return entity.CustomerId;
        }

        var existing = await db.Customers.FindAsync(input.CustomerId)
            ?? throw new BusinessRuleException("This customer no longer exists.");
        existing.Name = input.Name.Trim();
        existing.Country = input.Country.Trim();
        // The row version the user loaded: EF adds "WHERE RowVer = @original" to the UPDATE
        db.Entry(existing).Property(e => e.RowVer).OriginalValue = input.RowVer;
        await SaveAsync();
        return existing.CustomerId;
    }

    public async Task DeleteCustomerAsync(int id, byte[] rowVer)
    {
        if (await db.SampleRequests.AnyAsync(r => r.CustomerId == id))
            throw new BusinessRuleException("This customer has sample requests and cannot be deleted.");

        var entity = await db.Customers.FindAsync(id) ?? throw new BusinessRuleException("This customer no longer exists.");
        db.Entry(entity).Property(e => e.RowVer).OriginalValue = rowVer;
        db.Customers.Remove(entity);
        await SaveAsync();
    }

    // ---------- products ----------

    public Task<PagedResult<ProductRow>> ProductsAsync(string? search, string? material, int page)
    {
        var q = db.Products.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(p => p.Sku.Contains(s) || p.Name.Contains(s));
        }
        if (!string.IsNullOrWhiteSpace(material)) q = q.Where(p => p.Material == material);

        return q.OrderBy(p => p.Sku)
            .Select(p => new ProductRow(p.ProductId, p.Sku, p.Name, p.Material, p.Category, p.Plant!.Name,
                db.SampleRequests.Count(r => r.ProductId == p.ProductId)))
            .ToPagedAsync(page, PageSize);
    }

    public Task<ProductEntity?> ProductAsync(int id) =>
        db.Products.AsNoTracking().Include(p => p.Plant).FirstOrDefaultAsync(p => p.ProductId == id);

    public Task<List<PlantEntity>> PlantsAsync() =>
        db.Plants.AsNoTracking().OrderBy(p => p.Name).ToListAsync();

    public async Task<int> SaveProductAsync(ProductEntity input)
    {
        Validate(input);
        var sku = input.Sku.Trim().ToUpperInvariant();
        if (await db.Products.AnyAsync(p => p.Sku == sku && p.ProductId != input.ProductId))
            throw new BusinessRuleException($"SKU {sku} already exists.");

        ProductEntity entity;
        if (input.ProductId == 0)
        {
            entity = new ProductEntity();
            db.Products.Add(entity);
        }
        else
        {
            entity = await db.Products.FindAsync(input.ProductId)
                ?? throw new BusinessRuleException("This product no longer exists.");
            db.Entry(entity).Property(e => e.RowVer).OriginalValue = input.RowVer;
        }

        entity.Sku = sku;
        entity.Name = input.Name.Trim();
        entity.Material = input.Material;
        entity.Category = input.Category.Trim();
        entity.PlantId = input.PlantId;
        await SaveAsync();
        return entity.ProductId;
    }

    public async Task DeleteProductAsync(int id, byte[] rowVer)
    {
        if (await db.SampleRequests.AnyAsync(r => r.ProductId == id))
            throw new BusinessRuleException("This product has sample requests and cannot be deleted.");

        var entity = await db.Products.FindAsync(id) ?? throw new BusinessRuleException("This product no longer exists.");
        db.Entry(entity).Property(e => e.RowVer).OriginalValue = rowVer;
        db.Products.Remove(entity);
        await SaveAsync();
    }

    // ---------- shared ----------

    private async Task SaveAsync()
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            throw new BusinessRuleException("Someone else changed or deleted this record after you opened it. Reload and try again.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            db.ChangeTracker.Clear();
            throw new BusinessRuleException("A record with the same unique value already exists.");
        }
    }

    private static void Validate(CustomerEntity c)
    {
        if (string.IsNullOrWhiteSpace(c.Name) || c.Name.Length > 150) throw new BusinessRuleException("Customer name is required (max 150 characters).");
        if (string.IsNullOrWhiteSpace(c.Country) || c.Country.Length > 50) throw new BusinessRuleException("Country is required (max 50 characters).");
    }

    private static void Validate(ProductEntity p)
    {
        if (string.IsNullOrWhiteSpace(p.Sku) || p.Sku.Trim().Length > 30) throw new BusinessRuleException("SKU is required (max 30 characters).");
        if (string.IsNullOrWhiteSpace(p.Name) || p.Name.Length > 150) throw new BusinessRuleException("Product name is required (max 150 characters).");
        if (!Materials.All.Contains(p.Material)) throw new BusinessRuleException("Choose a valid material.");
        if (string.IsNullOrWhiteSpace(p.Category) || p.Category.Length > 50) throw new BusinessRuleException("Category is required (max 50 characters).");
        if (p.PlantId <= 0) throw new BusinessRuleException("Choose the producing plant.");
    }
}
