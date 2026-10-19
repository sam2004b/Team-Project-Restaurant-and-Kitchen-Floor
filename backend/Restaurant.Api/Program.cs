using Microsoft.EntityFrameworkCore;
using Restaurant.Api.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddDbContext<RestaurantDbContext>(
    options =>
    {
        var connectionString =
            builder.Configuration.GetConnectionString(
                "DefaultConnection");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "PostgreSQL connection string is missing."
            );
        }

        options.UseNpgsql(connectionString);
    }
);


builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.MapGet("/health/db", async (RestaurantDbContext db) =>
{
    bool connected = await db.Database.CanConnectAsync();

    if (!connected)
    {
        return Results.Problem(
            "PostgreSQL connection failed.",
            statusCode: 503
        );
    }

    return Results.Ok(new
    {
        database = "connected"
    });
});

app.Run();
