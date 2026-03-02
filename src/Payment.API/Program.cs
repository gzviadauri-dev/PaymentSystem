using System.Text;
using System.Threading.RateLimiting;
using Company.Observability;
using HealthChecks.UI.Client;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Payment.API.Endpoints;
using Payment.API.HealthChecks;
using Payment.Infrastructure;
using Payment.Infrastructure.Data;
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

// ── Infrastructure must be registered before --migrate so PaymentDbContext ─
// is available in the DI container when the migration branch calls Build(). ─
builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssembly(typeof(Payment.Application.Commands.TopUpBalanceCommand).Assembly));

builder.Services.AddInfrastructure(builder.Configuration);

// ── CLI --migrate mode: run migrations and exit ───────────────────────────
if (args.Contains("--migrate"))
{
    var migrateHost = builder.Build();
    using var scope = migrateHost.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<PaymentDbContext>().Database.MigrateAsync();
    Console.WriteLine("PaymentDb migrations applied successfully.");
    return;
}

// ── Observability ─────────────────────────────────────────────────────────
builder.AddCompanyObservability();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

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

builder.Services.AddAuthorization(opts =>
{
    opts.AddPolicy("AdminPolicy", policy =>
        policy.RequireAuthenticatedUser()
              .RequireClaim("role", "admin"));
});

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

builder.Services.AddHttpClient();

// ── Rate limiting on external callback endpoint ───────────────────────────
builder.Services.AddRateLimiter(opt =>
    opt.AddFixedWindowLimiter("ExternalCallback", o =>
    {
        o.PermitLimit = 100;
        o.Window = TimeSpan.FromMinutes(1);
        o.QueueLimit = 0;
    }));

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")!;
var rabbitHost = builder.Configuration["RabbitMQ:Host"] ?? "rabbitmq://localhost";
var rabbitUri = new Uri(rabbitHost.Replace("rabbitmq://", "amqp://"));

builder.Services.AddSingleton<RabbitMQ.Client.IConnectionFactory>(
    _ => new RabbitMQ.Client.ConnectionFactory { Uri = rabbitUri, AutomaticRecoveryEnabled = true });

builder.Services.AddSingleton<MassTransitBusHealthCheck>();

builder.Services.AddHealthChecks()
    .AddSqlServer(connectionString, name: "sqlserver", tags: ["db", "ready"])
    .AddRabbitMQ(name: "rabbitmq", tags: ["messaging", "ready"])
    .AddCheck<MassTransitBusHealthCheck>("masstransit", tags: ["messaging", "ready"])
    .AddCheck<OutboxLagHealthCheck>("outbox-lag", tags: ["ready"]);

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
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapBalanceEndpoints();
app.MapPaymentEndpoints();
app.MapExternalCallbackEndpoints();
app.MapAdminEndpoints();

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

public partial class Program { }
