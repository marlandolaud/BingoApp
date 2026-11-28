using BingoBongo.Data;
using BingoBongo.Games;
using BingoBongo.Live;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.Configure<AutoPilotOptions>(builder.Configuration.GetSection("AutoPilot"));
builder.Services.Configure<LiveOptions>(builder.Configuration.GetSection("Live"));
builder.Services.AddScoped<GameStore>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<GameEventHub>();
builder.Services.AddHostedService<PgNotificationListener>();
builder.Services.AddDbContext<BingoDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Bingo")));
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy
        .SetIsOriginAllowed(BingoBongo.LanCorsPolicy.IsAllowedOrigin)
        .AllowAnyHeader()
        .AllowAnyMethod());
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<BingoDbContext>().Database.Migrate();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseCors();

app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program { }
