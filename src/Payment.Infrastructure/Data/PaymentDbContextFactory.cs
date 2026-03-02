using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Payment.Infrastructure.Data;

/// <summary>
/// Design-time factory used by EF Core tooling (dotnet ef migrations).
/// Not used at runtime — the DbContext is registered through AddInfrastructure().
/// </summary>
public class PaymentDbContextFactory : IDesignTimeDbContextFactory<PaymentDbContext>
{
    public PaymentDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PaymentDbContext>()
            .UseSqlServer(
                "Server=localhost,1433;Database=PaymentDb;User Id=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=True;",
                sql => sql.MigrationsAssembly("Payment.Infrastructure"))
            .Options;
        return new PaymentDbContext(options);
    }
}
