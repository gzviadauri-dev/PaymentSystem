namespace LicenseCore.API.Entities;

public class Driver
{
    public Guid Id { get; set; }
    public Guid LicenseId { get; set; }
    public string FullName { get; set; } = null!;
    public bool IsActive { get; set; }
    public License License { get; set; } = null!;
}
