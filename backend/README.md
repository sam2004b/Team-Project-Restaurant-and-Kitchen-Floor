
# Restaurant & Kitchen Management — Backend

## Overview

This backend is part of the Restaurant and Kitchen Floor Management System developed for the Software Development Processes course.

The backend is built using ASP.NET Core Web API and provides the foundation for managing restaurant operations, including menus, orders, reservations, and kitchen workflows.

## Technology Stack

- C# / .NET 8
- ASP.NET Core Web API
- Entity Framework Core 8
- PostgreSQL
- Npgsql (EF Core PostgreSQL provider)
- Git and GitHub

## Prerequisites

Before running the backend, install:

1. .NET 8 SDK
2. PostgreSQL
3. pgAdmin 4 (optional database management tool)
4. Git

Verify that .NET is installed:

```powershell
dotnet --version
```

Install the EF Core CLI tool if not installed:

```powershell
dotnet tool install --global dotnet-ef --version "8.*"
```

## Project Structure

```text
backend/
├── RestaurantManagement.sln
├── Restaurant.Api/
│   ├── Controllers/
│   ├── Data/
│   │   └── RestaurantDbContext.cs
│   ├── Models/
│   │   └── Dish.cs
│   ├── Migrations/
│   ├── Program.cs
│   ├── appsettings.json
│   └── Restaurant.Api.csproj
└── README.md
```

## Local Setup

### 1. Clone the repository

```powershell
git clone https://github.com/sam2004b/Team-Project-Restaurant-and-Kitchen-Floor.git
cd Team-Project-Restaurant-and-Kitchen-Floor
```

### 2. Navigate to the backend

```powershell
cd backend/Restaurant.Api
```

### 3. Restore dependencies

```powershell
dotnet restore
```

### 4. Configure PostgreSQL

Make sure the PostgreSQL server is running.

Using pgAdmin, create:

- Database: `restaurant_db`
- Login role: `restaurant_app`
- Database owner: `restaurant_app`

Assign a private password to the login role.

The PostgreSQL port depends on your local installation. The default is 5432, but another port can be configured.

### 5. Configure the connection string

The application reads its PostgreSQL connection string from the configuration key:

`ConnectionStrings:DefaultConnection`

For local development, use .NET User Secrets to avoid committing passwords.

Initialize User Secrets if necessary:

```powershell
dotnet user-secrets init
```

Configure your database connection:

```powershell
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=restaurant_db;Username=restaurant_app;Password=YOUR_PASSWORD"
```

Replace `YOUR_PASSWORD` with your own database password.

Change `Port=5432` to your local PostgreSQL port if necessary.

The local connection string must not be committed to GitHub.

### 6. Apply database migrations

Set the environment to Development:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:DOTNET_ENVIRONMENT = "Development"
```

Apply existing migrations:

```powershell
dotnet ef database update
```

Entity Framework Core will create or update the necessary database tables.

The initial migration includes the database schema defined by the current EF Core models.

Do not manually create the application tables in pgAdmin.

### 7. Build the project

```powershell
dotnet build
```

Expected result:

`Build succeeded.`

### 8. Run the backend

```powershell
dotnet run
```

The terminal will display the local address where the API is running.

Example:

`http://localhost:5019`

The actual port may differ.

## Database Connection Health Check

The backend includes an endpoint to verify PostgreSQL connectivity.

Endpoint:

`GET /health/db`

Example URL:

`http://localhost:5019/health/db`

Expected response:

```json
{
  "database": "connected"
}
```

A successful response confirms that the API can connect to PostgreSQL.

The root URL (`/`) is not required to serve a webpage because this project is a Web API.

## Database Migrations

List available migrations:

```powershell
dotnet ef migrations list
```

Apply pending migrations:

```powershell
dotnet ef database update
```

When changing the EF Core database model, create a new migration with a descriptive name:

```powershell
dotnet ef migrations add MigrationName
```

Commit migration source files to Git so other developers can reproduce the schema.

Do not create the InitialCreate migration again if it already exists.

## Troubleshooting

### PostgreSQL connection refused

Check that PostgreSQL is running and that your connection string uses the correct host and port.

On Windows:

```powershell
Get-Service *postgres*
```

Test the configured port:

```powershell
Test-NetConnection localhost -Port 5432
```

Replace 5432 if your server uses another port.

### Password authentication failed

Verify that the database username and password are correct.

### Database does not exist

Create `restaurant_db` in pgAdmin and assign the appropriate database owner.

### Migration fails

Make sure:

- PostgreSQL is running.
- The connection string is configured.
- Your login has permission to modify the database schema.
- The correct environment is selected.

## Development Notes

- Use feature branches for development.
- Create pull requests before merging into the main branch.
- Never commit database passwords or other secrets.
- Keep EF Core migrations under version control.
- Coordinate model and schema changes with other backend developers.


## Docker Compose Setup (SK-04)

### Requirements

- Docker Desktop
- Docker Compose

PostgreSQL and the .NET SDK do not need to be
installed locally when using Docker Compose.

### Configuration

From the repository root, copy the environment template:

```powershell
Copy-Item .env.example .env
```

Set DB_NAME, DB_USER, and DB_PASSWORD in .env.

Do not commit .env to GitHub.

### Start the application

```powershell
docker compose up --build -d
```

### Check container status

```powershell
docker compose ps
```

### Test the API

Open:

http://localhost:5020/health/db

Expected response:

{
  "database": "connected"
}

### View logs

```powershell
docker compose logs api
docker compose logs db
```

### Stop the application

```powershell
docker compose down
```

### Restart

```powershell
docker compose up -d
```

### Database persistence

PostgreSQL data is stored in a named Docker volume.
Normal container restarts preserve database data.

Do not use docker compose down -v unless you
intentionally want to delete the database volume.

### Service ports

- API: localhost:5020
- PostgreSQL from Windows: localhost:5435
- PostgreSQL inside Docker network: db:5432
