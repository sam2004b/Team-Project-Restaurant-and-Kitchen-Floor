using Restaurant.Api.Security;
using Restaurant.Api.Services.Dishes;
using Restaurant.Api.Services.Waiter;
using Restaurant.Api.Services.Orders;
using Restaurant.Api.Hubs;
using Microsoft.OpenApi.Models;
using Restaurant.Api.Services.Menu;
using Microsoft.AspNetCore.Identity;
using Restaurant.Api.Seeding;
using Microsoft.EntityFrameworkCore;
using Restaurant.Api.Data;

var builder = WebApplication.CreateBuilder(args);

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

// ==========================================
// 2. ASP.NET CORE IDENTITY
// ==========================================

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
    .AddEntityFrameworkStores<RestaurantDbContext>()
    .AddSignInManager();

// ==========================================
// 3. AUTHENTICATION
// ==========================================

builder.Services
    .AddAuthentication(IdentityConstants.BearerScheme)
    .AddBearerToken(IdentityConstants.BearerScheme, options =>
    {
        options.BearerTokenExpiration = TimeSpan.FromHours(1);
    });

// ==========================================
// 4. AUTHORIZATION
// ==========================================

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RestaurantCreator", policy =>
        policy.RequireAuthenticatedUser()
              .RequireClaim(
                  "permission",
                  "restaurants:create"));

    StaffRolePolicies.Configure(options);
});

// ==========================================
// 5. RESTAURANT ADMINISTRATION SERVICES
// ==========================================

builder.Services.AddScoped<
    Restaurant.Api.Services.Administration.RestaurantAccessService>();

builder.Services.AddScoped<
    Restaurant.Api.Services.Administration.InvitationDeliveryService>();

// ==========================================
// 6. CORS CONFIGURATION
// ==========================================

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

// ==========================================
// 7. APPLICATION SERVICES
// ==========================================

builder.Services.AddScoped<
    IMenuReadService,
    MenuReadService>();

builder.Services.AddControllers();


builder.Services.AddSignalR();

builder.Services.AddScoped<
    IMenuUpdateNotifier,
    SignalRMenuUpdateNotifier>();


builder.Services.AddScoped<
    IDishAvailabilityService,
    DishAvailabilityService>();

builder.Services.AddScoped<
    WaiterTableQueryService>();

// S2-US12-T01: DB-backed order draft and send-state persistence.
builder.Services.AddScoped<OrderDraftPersistenceService>();
builder.Services.AddScoped<OrderDetailsQueryService>();


builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Restaurant & Kitchen Management API",
        Version = "v1",
        Description =
            "REST API for managing restaurant dishes, " +
            "customer menu availability, ingredients, " +
            "dish images, and administrative dish management."
    });

    // Define Bearer authentication in Swagger.
    options.AddSecurityDefinition("Bearer",
        new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            In = ParameterLocation.Header,
            Description =
                "Enter the access token obtained from /auth/login."
        });

    // Allow Swagger to send the token with API requests.
    options.AddSecurityRequirement(
        new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = "Bearer"
                    }
                },
                Array.Empty<string>()
            }
        });

    var xmlFile =
        $"{builder.Environment.ApplicationName}.xml";

    var xmlPath =
        Path.Combine(
            AppContext.BaseDirectory,
            xmlFile);

    options.IncludeXmlComments(xmlPath);
});

builder.Services.AddScoped<MenuAdminGuard>();

builder.Services.AddScoped<OrderDishAvailabilityValidator>();

builder.Services.AddScoped<StaffSessionValidator>();

// ==========================================
// 9. BUILD APPLICATION
// ==========================================

var app = builder.Build();

// ==========================================
// 10. APPLY DATABASE MIGRATIONS
// ==========================================

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

// ==========================================
// 11. DEVELOPMENT DATABASE SEEDING
// ==========================================

if (args.Contains(
    "--seed",
    StringComparer.OrdinalIgnoreCase))
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

    // Ensure the database schema is updated.
    await db.Database.MigrateAsync();

    // Create sample users, roles, restaurants,
    // memberships, invitations and dishes.
    await DatabaseSeeder.SeedAsync(
        scope.ServiceProvider,
        app.Configuration);

    return;
}

// ==========================================
// 12. SWAGGER MIDDLEWARE
// ==========================================

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// ==========================================
// 13. HTTP MIDDLEWARE
// ==========================================

app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseCors();

app.UseAuthentication();

// Validate the employee's current database session.
app.UseMiddleware<StaffSessionMiddleware>();

app.UseAuthorization();

app.MapControllers();

app.MapHub<MenuHub>("/hubs/menu");


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