namespace TelemetryApi.Data;

/// <summary>Den enda entiteten i exemplet - medvetet enkel, fokus ligger på telemetrin.</summary>
public class Customer
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? City { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
