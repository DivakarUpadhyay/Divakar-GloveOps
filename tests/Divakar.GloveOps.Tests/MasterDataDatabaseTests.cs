using Divakar.GloveOps.Core.MasterData;
using Divakar.GloveOps.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Divakar.GloveOps.Tests;

/// <summary>EF Core master-data service against the real database (skipped when it isn't available).</summary>
public sealed class MasterDataDatabaseTests : IDisposable
{
    private readonly List<int> _customers = [];
    private readonly List<int> _products = [];

    // A new DbContext per "request", like the web app's scoped lifetime
    private static GloveOpsDbContext NewDb() =>
        new(new DbContextOptionsBuilder<GloveOpsDbContext>().UseSqlServer(DatabaseFactAttribute.ConnectionString).Options);

    private static MasterDataService NewService() => new(NewDb());

    private async Task<CustomerEntity> NewCustomerAsync(string name = "Test Customer")
    {
        var id = await NewService().SaveCustomerAsync(new CustomerEntity { Name = $"{name} {Guid.NewGuid():N}"[..40], Country = "Taiwan" });
        _customers.Add(id);
        return (await NewService().CustomerAsync(id))!;
    }

    [DatabaseFact]
    public async Task Create_then_update_customer_with_rowversion()
    {
        var created = await NewCustomerAsync();
        Assert.NotEmpty(created.RowVer);

        created.Name = "Renamed Customer";
        await NewService().SaveCustomerAsync(created);

        var reloaded = await NewService().CustomerAsync(created.CustomerId);
        Assert.Equal("Renamed Customer", reloaded!.Name);
        Assert.NotEqual(created.RowVer, reloaded.RowVer);   // SQL Server bumped the ROWVERSION
    }

    [DatabaseFact]
    public async Task Stale_edit_is_refused_by_optimistic_concurrency()
    {
        var userA = await NewCustomerAsync();
        var userB = (await NewService().CustomerAsync(userA.CustomerId))!;   // both opened the same version

        userB.Country = "Japan";
        await NewService().SaveCustomerAsync(userB);                         // B saves first

        userA.Country = "Vietnam";
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => NewService().SaveCustomerAsync(userA));
        Assert.Contains("Someone else changed", ex.Message);
        Assert.Equal("Japan", (await NewService().CustomerAsync(userA.CustomerId))!.Country);
    }

    [DatabaseFact]
    public async Task Customer_with_sample_requests_cannot_be_deleted()
    {
        var used = (await NewService().CustomerAsync(1))!;    // seed customer 1 has requests
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => NewService().DeleteCustomerAsync(1, used.RowVer));
        Assert.Contains("has sample requests", ex.Message);
    }

    [DatabaseFact]
    public async Task Unused_customer_can_be_deleted()
    {
        var c = await NewCustomerAsync();
        await NewService().DeleteCustomerAsync(c.CustomerId, c.RowVer);
        Assert.Null(await NewService().CustomerAsync(c.CustomerId));
        _customers.Remove(c.CustomerId);
    }

    [DatabaseFact]
    public async Task Duplicate_sku_is_refused_and_sku_is_upper_cased()
    {
        var sku = $"t-{Guid.NewGuid():N}"[..12];
        var id = await NewService().SaveProductAsync(new ProductEntity
        {
            Sku = sku, Name = "Test glove", Material = "NBR", Category = "Medical", PlantId = 1
        });
        _products.Add(id);
        Assert.Equal(sku.ToUpperInvariant(), (await NewService().ProductAsync(id))!.Sku);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => NewService().SaveProductAsync(new ProductEntity
        {
            Sku = sku.ToUpperInvariant(), Name = "Copy", Material = "PVC", Category = "Food", PlantId = 2
        }));
        Assert.Contains("already exists", ex.Message);
    }

    [DatabaseFact]
    public async Task Product_list_filters_pages_and_counts_requests()
    {
        var page = await NewService().ProductsAsync(search: null, material: "NBR", page: 1);
        Assert.All(page.Items, p => Assert.Equal("NBR", p.Material));
        Assert.True(page.TotalCount >= 4);                      // 4 NBR products in the seed
        Assert.Contains(page.Items, p => p.Sku == "NBR-CH-200" && p.RequestCount >= 1 && p.PlantName == "Binh Duong Plant");
    }

    [Fact]
    public async Task Validation_runs_before_the_database()
    {
        var service = new MasterDataService(NewDb());   // no query is sent: validation fails first
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.SaveProductAsync(new ProductEntity { Sku = "X", Name = "Y", Material = "WOOD", Category = "Z", PlantId = 1 }));
    }

    public void Dispose()
    {
        using var db = NewDb();
        if (_products.Count > 0) db.Products.Where(p => _products.Contains(p.ProductId)).ExecuteDelete();
        if (_customers.Count > 0) db.Customers.Where(c => _customers.Contains(c.CustomerId)).ExecuteDelete();
    }
}
