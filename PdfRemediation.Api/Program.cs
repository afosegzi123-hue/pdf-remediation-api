using AspNetCoreRateLimit;
using PdfRemediation.Api.Security;
using PdfRemediation.Api.Services;
using PdfRemediation.Api.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Database
var dbConnectionString = builder.Configuration["DB_CONNECTION_STRING"];
if (!string.IsNullOrEmpty(dbConnectionString))
{
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseNpgsql(dbConnectionString));
}

// Supabase
builder.Services.AddSingleton<SupabaseService>();
builder.Services.AddSingleton<HeuristicPdfEngine>();

// Security
builder.Services.AddSecurityServices(builder.Configuration);

var app = builder.Build();

// Init Supabase
var supabase = app.Services.GetRequiredService<SupabaseService>();
await supabase.InitializeAsync();

// Middleware
app.UseCors("AllowVercel");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Ensure DB is created
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetService<AppDbContext>();
    if (dbContext != null)
    {
        try { dbContext.Database.EnsureCreated(); }
        catch (Exception ex) { Console.WriteLine("DB EnsureCreated Failed: " + ex.Message); }
    }
}

// Health check for Render
app.MapGet("/api/health", () => Results.Ok(new { status = "healthy" }));

app.Run();
