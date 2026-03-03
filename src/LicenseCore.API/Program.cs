using System.Text;
using Company.Observability;
using HealthChecks.UI.Client;
using LicenseCore.Application.Consumers;
using LicenseCore.Application.Interfaces;
using LicenseCore.API.Data;
using LicenseCore.API.Endpoints;
using LicenseCore.API.HealthChecks;
using LicenseCore.API.Services;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// ── JWT key entropy guard ─────────────────────────────────────────────────
var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Jwt:Key is not configured.");
if (jwtKey.Length < 32)
    throw new InvalidOperationException(
        $"Jwt:Key must be >= 32 characters (currently {jwtKey.Length}). " +
        "Generate one with: openssl rand -base64 32");
var weakKeys = new[] { "changeme", "secret", "your-secret-key", "development-key", "password", "supersecret" };
if (weakKeys.Any(w => jwtKey.Contains(w, StringComparison.OrdinalIgnoreCase)))
    throw new InvalidOperationException("Jwt:Key matches a known insecure placeholder.");
if (builder.Environment.IsProduction() && jwtKey.Contains("dev", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Development key detected in production environment.");

// ── DbContext must be registered before --migrate ─────────────────────────
builder.Services.AddDbContext<LicenseCoreDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sqlOptions =>
        {
            sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(30),
                errorNumbersToAdd: null);
            sqlOptions.CommandTimeout(30);
        }));

// ── CLI --migrate mode: run migrations and exit ───────────────────────────
if (args.Contains("--migrate"))
{
    var migrateHost = builder.Build();
    using var scope = migrateHost.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<LicenseCoreDbContext>().Database.MigrateAsync();
    Console.WriteLine("LicenseCoreDb migrations applied successfully.");
    return;
}

// ── Redis: shared cache + distributed lock for MonthlyDebtGeneratorService ─
var redisConnection = builder.Configuration.GetConnectionString("Redis")
    ?? throw new InvalidOperationException("Redis connection string not configured.");

builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = redisConnection;
    options.InstanceName = "PaymentSystem:";
});

builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(redisConnection));

builder.Services.AddSingleton<IDistributedLockFactory, RedisDistributedLockFactory>();

// ── Observability ─────────────────────────────────────────────────────────
builder.AddCompanyObservability();

builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics.AddMeter("LicenseCore.API"));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddScoped<ILicenseRepository, LicenseCoreRepository>();
builder.Services.AddScoped<INotificationService, LoggingNotificationService>();
builder.Services.AddHostedService<MonthlyDebtGeneratorService>();

builder.Services.AddMassTransit(x =>
{
    x.AddEntityFrameworkOutbox<LicenseCoreDbContext>(o =>
    {
        o.UseSqlServer();
        o.UseBusOutbox();
    });

    x.AddConsumer<PaymentCompletedConsumer>();
    x.AddConsumer<SendNotificationConsumer, SendNotificationConsumerDefinition>();

    x.UsingRabbitMq((ctx, cfg) =>
    {
        var rabbitHost = builder.Configuration["RabbitMQ:Host"] ?? "rabbitmq://localhost";
        cfg.Host(rabbitHost);

        cfg.UseMessageRetry(r => r.Exponential(
            5,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(5)));

        cfg.ConfigureEndpoints(ctx);
    });
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });

builder.Services.AddAuthorization();

// ── CORS — enforce HTTPS origins in production ────────────────────────────
var origins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>()
              ?? ["http://localhost:3000"];
if (builder.Environment.IsProduction())
{
    var insecure = origins.Where(o => !o.StartsWith("https://", StringComparison.OrdinalIgnoreCase)).ToArray();
    if (insecure.Length > 0)
        throw new InvalidOperationException(
            $"Non-HTTPS CORS origins detected in production: {string.Join(", ", insecure)}");
}
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(origins)
              .WithMethods("GET", "POST")
              .WithHeaders("Authorization", "Content-Type", "Idempotency-Key", "X-Correlation-Id")
              .DisallowCredentials()));

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")!;

builder.Services.AddSingleton<MassTransitBusHealthCheck>();

builder.Services.AddHealthChecks()
    .AddSqlServer(connectionString, name: "sqlserver", tags: ["db", "ready"])
    .AddCheck<MassTransitBusHealthCheck>("masstransit", tags: ["messaging", "ready"]);

var app = builder.Build();

// ── Redis startup validation ──────────────────────────────────────────────
var redis = app.Services.GetRequiredService<IConnectionMultiplexer>();
if (!redis.IsConnected)
    throw new InvalidOperationException(
        "Redis is not connected. Check ConnectionStrings:Redis.");

// ── Global exception handler ─────────────────────────────────────────────
app.UseExceptionHandler(exceptionHandlerApp =>
{
    exceptionHandlerApp.Run(async context =>
    {
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new
        {
            error = "An unexpected error occurred. Please try again later."
        });
    });
});

app.UseCompanyObservability();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapLicenseEndpoints();
app.MapVehicleEndpoints();
app.MapDriverEndpoints();

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
}).AllowAnonymous();

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
}).AllowAnonymous();

app.Run();
