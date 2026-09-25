using Microsoft.EntityFrameworkCore;

namespace CQRSWorker.Data;

/// <summary>
/// Workern äger databasen: den skapar schemat och är den ENDA som skriver.
/// API:t läser samma tabell med Dapper, så tabell- och kolumnnamnen här är
/// också läs-sidans kontrakt.
/// </summary>
public class PersonsDbContext(DbContextOptions<PersonsDbContext> options) : DbContext(options)
{
    public DbSet<Person> Persons => Set<Person>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Person>(e =>
        {
            e.ToTable("Persons");
            e.HasKey(p => p.Id);

            // Guid lagras som läsbar text (varchar(36)). Då blir den enkel att
            // läsa för Dapper på API-sidan och lätt att se i en SQL-klient.
            e.Property(p => p.Id).HasConversion<string>().HasMaxLength(36);

            e.Property(p => p.FirstName).HasMaxLength(100).IsRequired();
            e.Property(p => p.LastName).HasMaxLength(100).IsRequired();
            e.Property(p => p.Email).HasMaxLength(200);
        });
    }
}
