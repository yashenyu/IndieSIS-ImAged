using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Imaged Backend API", Version = "v1" });
});

// Very permissive CORS for local demo (allows login-site to call)
builder.Services.AddCors(options =>
{
    options.AddPolicy("LocalAllow", p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var app = builder.Build();

app.UseCors("LocalAllow");

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();

app.MapGet("/", () => "ImagedBackend running");

app.Run();
