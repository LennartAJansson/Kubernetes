namespace CQRSWorker.Data;

/// <summary>Skrivmodellen - den enda entiteten i den här demon.</summary>
public class Person
{
    public Guid Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string? Email { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
