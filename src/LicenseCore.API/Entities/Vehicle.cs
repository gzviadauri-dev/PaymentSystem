namespace LicenseCore.API.Entities;

public class Vehicle
{
    public Guid Id { get; set; }
    public Guid LicenseId { get; set; }
    public string PlateNumber { get; set; } = null!;
    public bool IsActive { get; set; }
    public License License { get; set; } = null!;
}
