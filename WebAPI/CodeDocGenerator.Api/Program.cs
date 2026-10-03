using Npgsql;
using CodeDocGenerator.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Database
var dataSourceBuilder = new NpgsqlDataSourceBuilder(
    builder.Configuration.GetConnectionString("DefaultConnection"));
builder.Services.AddSingleton(dataSourceBuilder.Build());

// Services
builder.Services.AddSingleton<RoslynAnalysisService>();
builder.Services.AddHttpClient<DocGenerationService>();
builder.Services.AddScoped<DocumentationService>();

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngular", policy =>
        policy.WithOrigins("http://localhost:4200")
              .AllowAnyHeader()
              .AllowAnyMethod());
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowAngular");
app.MapControllers();

app.Run();