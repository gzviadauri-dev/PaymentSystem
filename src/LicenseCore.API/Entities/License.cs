namespace LicenseCore.API.Entities;

public class License
{
    public Guid Id { get; set; }
    public string OwnerName { get; set; } = null!;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Vehicle? Vehicle { get; set; }
    public List<Driver> Drivers { get; set; } = [];

    public bool HasVehicle => Vehicle is not null;
    public int DriverCount => Drivers.Count;
}
