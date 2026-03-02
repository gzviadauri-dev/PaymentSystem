using Microsoft.EntityFrameworkCore;
using Payment.Infrastructure.Data;
using Testcontainers.MsSql;

namespace Payment.Tests.Infrastructure;

public class SqlServerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public PaymentDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PaymentDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        return new PaymentDbContext(options);
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.StopAsync();
    }
}
