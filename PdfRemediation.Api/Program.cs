using AspNetCoreRateLimit;
using PdfRemediation.Api.Security;
using PdfRemediation.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

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

// Health check for Render
app.MapGet("/api/health", () => Results.Ok(new { status = "healthy" }));

app.Run();
