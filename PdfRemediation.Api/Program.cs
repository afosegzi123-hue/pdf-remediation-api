using Microsoft.EntityFrameworkCore;
using PdfRemediation.Api.Data;
// Force load the BouncyCastle adapter assembly so iText reflection finds it
_ = typeof(iText.Bouncycastle.BouncyCastleFactoryCreator);

var builder = WebApplication.CreateBuilder(args);

// Explicitly disable reloadOnChange to prevent inotify exhaustion (Status 139) on Render
builder.Configuration.Sources.Clear();
builder.Configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
builder.Configuration.AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: false);
builder.Configuration.AddEnvironmentVariables();

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");

if (string.IsNullOrEmpty(connectionString))
{
    Console.WriteLine("WARNING: ConnectionString is null or empty. Database will fail to connect.");
}

builder.Services.AddDbContext<AppDbContext>(options =>
{
    if (!string.IsNullOrEmpty(connectionString))
    {
        options.UseNpgsql(connectionString);
    }
});

builder.Services.AddScoped<PdfRemediation.Api.Services.IBatchWorkflowService, PdfRemediation.Api.Services.BatchWorkflowService>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend",
        policy =>
        {
            policy.WithOrigins("https://pdf-remediation-api-moon.vercel.app", "http://localhost:3000")
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .WithExposedHeaders("Content-Disposition");
        });
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddControllers();

var app = builder.Build();

// Automatically create database schema in the background to prevent blocking Kestrel startup
Task.Run(() => 
{
    try
    {
        using (var scope = app.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (!string.IsNullOrEmpty(connectionString)) 
            {
                Console.WriteLine("Attempting to run Database.EnsureCreated() in background...");
                dbContext.Database.EnsureCreated();
                Console.WriteLine("Database schema verified/created successfully.");
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"CRITICAL STARTUP ERROR during EnsureCreated: {ex}");
    }
});

// Simple health check endpoint for Render to hit
app.MapGet("/", () => "PDF Remediation API is Live!");
app.MapGet("/api/health", () => Results.Ok(new { status = "healthy", timestamp = DateTimeOffset.UtcNow }));

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Render handles HTTPS termination, so we don't redirect internally
// app.UseHttpsRedirection();

// Use CORS before Authorization and Controllers
app.UseCors("AllowFrontend");

app.UseAuthorization();

app.MapControllers();

app.Run();
