using Microsoft.EntityFrameworkCore;

namespace TelemetryApi.Data;

public class CustomersDbContext(DbContextOptions<CustomersDbContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>(e =>
        {
            e.ToTable("Customers");
            e.HasKey(c => c.Id);

            // Guid som läsbar text (varchar(36)) - lätt att se i en SQL-klient.
            e.Property(c => c.Id).HasConversion<string>().HasMaxLength(36);

            e.Property(c => c.Name).HasMaxLength(200).IsRequired();
            e.Property(c => c.Email).HasMaxLength(200);
            e.Property(c => c.Phone).HasMaxLength(50);
            e.Property(c => c.City).HasMaxLength(100);
            e.HasIndex(c => c.Name);
        });
    }
}
