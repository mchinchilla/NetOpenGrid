using Microsoft.EntityFrameworkCore;

namespace NetOpenGrid.Example.Samples;

public sealed class ShopDb(DbContextOptions<ShopDb> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var order = modelBuilder.Entity<Order>();
        order.HasKey(o => o.Id);
        order.HasIndex(o => o.Number).IsUnique();
    }
}
