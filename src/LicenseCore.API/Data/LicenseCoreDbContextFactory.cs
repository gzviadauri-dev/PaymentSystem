using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LicenseCore.API.Data;

/// <summary>
/// Design-time factory used by EF Core tooling (dotnet ef migrations).
/// </summary>
public class LicenseCoreDbContextFactory : IDesignTimeDbContextFactory<LicenseCoreDbContext>
{
    public LicenseCoreDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<LicenseCoreDbContext>()
            .UseSqlServer(
                "Server=localhost,1433;Database=LicenseCoreDb;User Id=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=True;")
            .Options;
        return new LicenseCoreDbContext(options);
    }
}
