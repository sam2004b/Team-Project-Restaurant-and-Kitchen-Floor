using Microsoft.AspNetCore.Identity;
using Restaurant.Api.Seeding;
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



builder.Services
    .AddIdentityCore<IdentityUser>(options =>
    {
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;
        options.Password.RequiredLength = 10;

        options.User.RequireUniqueEmail = true;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<RestaurantDbContext>();

var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? ["http://localhost:5173"];

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod());
});


builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment() &&
    app.Configuration.GetValue<bool>(
        "Database:ApplyMigrationsOnStartup"))
{
    await using var scope =
        app.Services.CreateAsyncScope();

    var db = scope.ServiceProvider
        .GetRequiredService<RestaurantDbContext>();

    await db.Database.MigrateAsync();
}



if (args.Contains("--seed", StringComparer.OrdinalIgnoreCase))
{
    if (!app.Environment.IsDevelopment())
    {
        throw new InvalidOperationException(
            "Database seeding is allowed only in Development."
        );
    }

    await using var scope =
        app.Services.CreateAsyncScope();

    var db = scope.ServiceProvider
        .GetRequiredService<RestaurantDbContext>();

    // Ensure database schema is updated first
    await db.Database.MigrateAsync();

    // Create sample users, roles and dishes
    await DatabaseSeeder.SeedAsync(
        scope.ServiceProvider,
        app.Configuration);

    return;
}


/*// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}*/

app.UseHttpsRedirection();

app.UseCors();

app.UseAuthorization();

app.MapControllers();

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok"
}));

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
