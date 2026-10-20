
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


# SK-06 — Development Database Seeding

## 1. Overview

This section describes the development database seeding implementation for the Restaurant Floor and Kitchen Management System.

The purpose of SK-06 is to provide initial data for local development and testing, allowing team members to work with predefined users, roles, and menu items without manually inserting records into PostgreSQL.

The seeding mechanism is implemented using ASP.NET Core 8, Entity Framework Core, ASP.NET Core Identity, and PostgreSQL.

**Key features:**
- Automatic creation of predefined user roles.
- Creation of development user accounts.
- Assignment of users to their respective roles.
- Insertion of initial restaurant menu items.
- Idempotent seeding to prevent duplicate records during repeated sequential executions.
- Support for both local PostgreSQL and Docker Compose environments.

## 2. Seeded User Roles

The application supports the following development roles:

| Role | Description |
|---|---|
| Administrator | Manages restaurant administration and configuration |
| Waiter | Handles restaurant floor and customer-order operations |
| Chef | Manages kitchen operations and dish availability |
| Reception Manager | Handles reception and reservation-related operations |

These roles are intended to support the application's planned role-based functionality.

## 3. Development Test Accounts

The seed script creates the following test accounts:

| Username | Email | Role |
|---|---|---|
| admin | admin@restaurant.test | Administrator |
| waiter | waiter@restaurant.test | Waiter |
| chef | chef@restaurant.test | Chef |
| reception | reception@restaurant.test | Reception Manager |

All development accounts use a password supplied through the `Seed:DefaultPassword` configuration setting.

ASP.NET Core Identity securely hashes account passwords before storing them.

**Security note:** Development credentials must never be committed to GitHub or used in production environments.

## 4. Initial Restaurant Menu Data

The seeder inserts the following example dishes if they do not already exist:

| Dish | Category | Price | Available |
|---|---|---:|---|
| Margherita Pizza | Pizza | 9.90 | Yes |
| Chicken Burger | Burgers | 12.50 | Yes |
| Caesar Salad | Salads | 8.00 | Yes |
| Fish Soup | Soups | 7.25 | No |

These items are demonstration data intended for testing menu-related functionality.

They support future tasks such as displaying the menu, managing dishes, and controlling dish availability.

The prices are sample numeric values; no real restaurant currency is assumed.

## 5. Implementation Structure

The seeding implementation uses the following components:

**`RestaurantDbContext.cs`**

Configures Entity Framework Core database access and the ASP.NET Core Identity tables.

**`DatabaseSeeder.cs`**

Contains the logic responsible for creating roles, test users, role assignments, and sample dishes.

**`Program.cs`**

Recognizes the `--seed` command, applies pending migrations, and executes the seeder.

**`Migrations/`**

Contains EF Core migrations required to create and update the PostgreSQL database schema.

## 6. Running the Seeder Locally

### Prerequisites

- .NET 8 SDK
- PostgreSQL
- Configured `ConnectionStrings:DefaultConnection`
- Applied EF Core migrations or permission to apply them

### Step 1 — Navigate to the API project

From the repository root:

```powershell
cd backend/Restaurant.Api
```

### Step 2 — Configure the development password

Use .NET User Secrets:

```powershell
dotnet user-secrets set "Seed:DefaultPassword" "YOUR_PRIVATE_DEVELOPMENT_PASSWORD"
```

Replace the placeholder with a strong password.

The database connection string must also be configured through User Secrets or environment variables.

### Step 3 — Set the Development environment

In PowerShell:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:DOTNET_ENVIRONMENT = "Development"
```

### Step 4 — Run the seed command

```powershell
dotnet run -- --seed
```

The application applies pending database migrations and creates missing development data.

Expected output:

```text
Database seeding completed.
```

The process exits after seeding without starting the API server.

## 7. Running the Seeder with Docker Compose

Docker Compose can execute the same seeding logic using the containerized PostgreSQL database.

### Step 1 — Configure environment variables

From the repository root, create a local `.env` file if it does not already exist:

```powershell
Copy-Item .env.example .env
```

Configure the PostgreSQL environment variables and add:

```dotenv
SEED_PASSWORD=YOUR_PRIVATE_DEVELOPMENT_PASSWORD
```

The `compose.yaml` API service must map this variable to the .NET configuration key:

```yaml
Seed__DefaultPassword: ${SEED_PASSWORD}
```

Do not commit the `.env` file.

### Step 2 — Start Docker Compose

From the repository root:

```powershell
docker compose up --build -d
```

### Step 3 — Execute the seed command

```powershell
docker compose run --rm api --seed
```

Expected output:

```text
Database seeding completed.
```

The Docker seeder connects to PostgreSQL using the connection string provided by Docker Compose.

**Note:** The local PostgreSQL database and Docker PostgreSQL database are independent. Each must be seeded separately if both are used for development.

## 8. Verification

To verify the seeded data inside Docker, open the PostgreSQL terminal:

```powershell
docker compose exec db psql -U restaurant_app -d restaurant_db
```

### Verify users

```sql
SELECT "UserName", "Email"
FROM "AspNetUsers";
```

### Verify roles

```sql
SELECT "Name"
FROM "AspNetRoles";
```

### Verify user-role assignments

```sql
SELECT
    u."UserName",
    r."Name" AS "Role"
FROM "AspNetUsers" u
JOIN "AspNetUserRoles" ur
    ON u."Id" = ur."UserId"
JOIN "AspNetRoles" r
    ON ur."RoleId" = r."Id";
```

### Verify sample menu data

```sql
SELECT "Id", "Name", "Price", "IsAvailable"
FROM dishes;
```

Exit PostgreSQL using:

```text
\q
```

## 9. Idempotency Testing

The seed operation is designed to be idempotent.

This means executing it multiple times should not create duplicate users, roles, user-role assignments, or sample dishes.

### Test procedure

1. Run the seeder on an empty development database.
2. Verify the initial records.
3. Execute the seeder a second time.
4. Compare the database record counts.
5. Confirm that no duplicate records were created.

Run the seeder again:

```powershell
docker compose run --rm api --seed
```

Check database counts:

```sql
SELECT 'Users' AS item, COUNT(*) FROM "AspNetUsers"
UNION ALL
SELECT 'Roles', COUNT(*) FROM "AspNetRoles"
UNION ALL
SELECT 'User Role Links', COUNT(*) FROM "AspNetUserRoles"
UNION ALL
SELECT 'Dishes', COUNT(*) FROM dishes;
```

For a database initially containing no development records, the expected counts are:

| Data | Expected records |
|---|---:|
| Users | 4 |
| Roles | 4 |
| User-role assignments | 4 |
| Sample dishes | 4 |

The counts should remain unchanged after running the seed operation a second time.

Existing unrelated database records should not be deleted or overwritten.

## 10. Security Considerations

- Seeding is restricted to the Development environment.
- Passwords are configured using User Secrets or local environment variables.
- Passwords are stored as Identity password hashes, not plain text.
- Development accounts must not be deployed to production.
- Local `.env` files and credentials must not be committed to GitHub.
- Database migrations and seed source code should be maintained under version control.

## 11. Troubleshooting

**Error: Seed:DefaultPassword is not configured**

Ensure that the seed password is configured in User Secrets or through the Docker Compose environment.

**Error: Database connection failed**

Verify that PostgreSQL is running and the connection string points to the correct host, port, and database.

**Error: Identity tables do not exist**

Apply the required EF Core migrations:

```powershell
dotnet ef database update
```

**Error: Password does not meet requirements**

Use a strong password satisfying the ASP.NET Core Identity password policy.

**Duplicate records after seeding**

Review the existence checks in `DatabaseSeeder.cs` and verify that no duplicate seed records already exist.

The seeding mechanism provides reusable development data for upcoming restaurant management features.



## US-1-T01 — Electronic Menu Data Model and Query

### Overview

Implemented the data retrieval layer for US-1 (View Electronic Menu).

The purpose of this task is to provide a reusable service that retrieves available restaurant dishes using Entity Framework Core.

### Implementation

The following components were implemented:

- **MenuDishDto.cs:** Defines the data returned for each menu dish.
- **IMenuReadService.cs:** Defines the menu retrieval contract.
- **MenuReadService.cs:** Implements the EF Core query for available dishes.
- **MenuReadServiceTests.cs:** Contains automated tests for menu retrieval.

### Query Functionality

The menu query:

- Retrieves only dishes marked as available.
- Returns dish ID, name, price, description, category and availability.
- Uses `AsNoTracking()` for read-only queries.
- Orders dishes by name and ID.
- Returns an empty collection when no dishes are available.
- Reflects dish availability changes in subsequent queries.

### Testing

Run the automated tests from the repository root:

`dotnet test backend/RestaurantManagement.sln`

The tests cover:

1. Empty menu.
2. Menu containing 12 available dishes.
3. Menu containing 50 available dishes.
4. Exclusion of unavailable dishes.
5. Dish availability changes.

**Verified results:**

- Build succeeded.
- 6 tests passed.
- 0 tests failed.
- 0 tests skipped.

Automated query tests use EF Core InMemory. PostgreSQL seed data can be verified separately using Docker Compose.

### Integration with US-1-T02

The `IMenuReadService` interface is registered through dependency injection.

It can be consumed by the menu API endpoint implemented by Sambhav under US-1-T02.

This separation avoids duplicating EF Core query logic inside the API controller.


## US-3-T01 — Dish Availability Data and Query

### Overview

Implemented the data/query functionality supporting US-3: Show and Order Only Available Dishes.

The goal is to ensure that the application can distinguish available, unavailable, and nonexistent dishes while providing an available-dishes query for the customer menu.

### Database Model

The existing `Dish` entity contains the `IsAvailable` property.

- `true`: The dish is available.
- `false`: The dish is unavailable.

The availability flag is stored in the existing PostgreSQL `dishes` table and configured using Entity Framework Core.

No additional database migration was required because the availability field already existed.

### Implemented Query Methods

**GetAvailableDishesAsync()**

Retrieves currently available dishes using Entity Framework Core.

The query excludes dishes marked unavailable and uses `AsNoTracking()` for read-only access.

**GetDishAvailabilityAsync(int dishId)**

Retrieves the availability status of a specific dish.

Possible results:

- `true`: Dish exists and is available.
- `false`: Dish exists and is unavailable.
- `null`: Dish does not exist.

### Automated Tests

The `DishAvailabilityTests.cs` file contains tests for:

1. Available dishes.
2. Unavailable dishes.
3. Nonexistent dishes.
4. Dishes becoming unavailable.
5. Dishes becoming available again.

Run tests from the repository root:

`dotnet test backend/RestaurantManagement.sln`

### PostgreSQL Verification

The availability column and filtering behavior can be verified through PostgreSQL using Docker Compose.

The development database includes sample available and unavailable dishes.

### Integration

The query functionality can be reused by US-3-T02, where the backend API must reject new orders containing unavailable dishes.

Server-side order validation and HTTP error responses are outside the scope of US-3-T01.