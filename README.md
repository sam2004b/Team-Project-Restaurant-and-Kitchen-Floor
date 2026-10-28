# Restaurant Floor & Kitchen Management System

A full-stack restaurant management application built with **ASP.NET Core 8, React, TypeScript, and PostgreSQL**.

The application provides an electronic customer menu and an interface for managing dishes, ingredients, availability, and photographs.

The complete application is containerized using Docker Compose, allowing developers to run the frontend, backend, and database together.

## Table of Contents

1. [Technologies](#technologies)
2. [Requirements](#requirements)
3. [Getting Started with Docker](#getting-started-with-docker)
4. [Application URLs](#application-urls)
5. [Available Features](#available-features)
6. [Useful Docker Commands](#useful-docker-commands)
7. [Project Structure](#project-structure)
8. [Important Notes](#important-notes)

## Technologies

| Component | Technology |
|---|---|
| Frontend | React, TypeScript, Vite |
| Backend | ASP.NET Core 8 Web API |
| Database | PostgreSQL 16 |
| ORM | Entity Framework Core |
| Frontend web server | Nginx |
| Containerization | Docker & Docker Compose |
| API documentation | Swagger / OpenAPI |

## Requirements

To run the application locally, you only need:

- **Docker Desktop** with Docker Compose
- An internet connection for the initial Docker image and dependency downloads
- A copy of this repository

**You do not need to install Node.js, npm, .NET SDK, PostgreSQL, or Nginx separately.** These tools run inside Docker containers.

## Getting Started with Docker

### 1. Get the project

Clone the repository:

```bash
git clone https://github.com/sam2004b/Team-Project-Restaurant-and-Kitchen-Floor.git
cd Team-Project-Restaurant-and-Kitchen-Floor
```

Alternatively, download the project ZIP from GitHub and extract it.

### 2. Configure environment variables

Create a `.env` file from the provided `.env.example`.

On Windows PowerShell:

```powershell
Copy-Item .env.example .env
```

On macOS or Linux:

```bash
cp .env.example .env
```

Open `.env` and configure the database and development seed passwords:

```env
DB_NAME=restaurant_db
DB_USER=restaurant_app
DB_PASSWORD=your_private_database_password
SEED_PASSWORD=your_private_development_password
```

Choose strong passwords. The seed password must satisfy ASP.NET Core Identity's configured password requirements.

**Never commit `.env` or real passwords to GitHub.**

### 3. Build and start the application

Make sure Docker Desktop is running.

From the project root, execute:

```bash
docker compose up --build -d
```

This command builds and starts three services:

- **db:** PostgreSQL database
- **api:** ASP.NET Core backend
- **frontend:** React application served through Nginx

Docker Compose creates the internal network automatically.

### 4. Initialize sample data

For a new development database, run:

```bash
docker compose run --rm api --seed
```

This populates the database with example dishes, ingredients, image references, and development test roles/accounts.

The seeder is designed to avoid duplicating existing sample records.

### 5. Open the application

Once all containers are running, open:

**Customer Menu:** http://localhost:5173

The application is now ready for local use.

## Application URLs

| Service | URL |
|---|---|
| Customer Menu | http://localhost:5173 |
| Administrator Dish Management | http://localhost:5173/admin/dishes |
| Backend API | http://localhost:5020 |
| API Documentation (Swagger) | http://localhost:5020/swagger |
| API through Nginx | http://localhost:5173/api/dishes |
| Database Health Check | http://localhost:5020/health/db |

Frontend requests under `/api/` are forwarded by Nginx to the backend container.

For example:

`http://localhost:5173/api/dishes`

is forwarded internally to:

`http://api:8080/dishes`

This allows the frontend and backend to communicate through Docker without hard-coded machine-specific IP addresses.

## Available Features

### Customer Menu

- View available dishes.
- Browse dishes by category.
- View dish prices, descriptions, and ingredients.
- View dish photographs.
- Refresh the menu to retrieve updated information.
- Handle loading and API errors.

### Dish Management

- Create new dishes.
- Edit existing dish information.
- Manage ingredients.
- Change dish availability.
- Upload and replace dish photographs.
- Retrieve dish details and manage the dish list.

### Backend

- REST API for menu and dish management.
- PostgreSQL integration using Entity Framework Core.
- Database migrations.
- Development seed data.
- Swagger API documentation.
- Persistent PostgreSQL and uploaded-image Docker volumes.

## Useful Docker Commands

All commands should be run from the project root.

**Start the application:**

```bash
docker compose up -d
```

**Rebuild and start after code changes:**

```bash
docker compose up --build -d
```

**View running services:**

```bash
docker compose ps
```

**View backend logs:**

```bash
docker compose logs --tail=100 api
```

**View frontend logs:**

```bash
docker compose logs --tail=100 frontend
```

**Stop the application:**

```bash
docker compose down
```

**Restart the application:**

```bash
docker compose up -d
```

Docker volumes preserve database data and uploaded images when containers are normally stopped or recreated.

**Warning:** Avoid `docker compose down -v` unless you intentionally want to delete stored Docker volume data.

## Project Structure

```text
Restaurant/
├── compose.yaml
├── .env.example
├── README.md
│
├── backend/
│   ├── README.md
│   ├── RestaurantManagement.sln
│   ├── Restaurant.Api/
│   │   ├── Dockerfile
│   │   ├── Controllers/
│   │   ├── Data/
│   │   ├── Dtos/
│   │   ├── Models/
│   │   ├── Services/
│   │   ├── Migrations/
│   │   └── wwwroot/
│   └── Restaurant.Api.Tests/
│
└── FrontEnd/
    └── React/
        ├── Dockerfile
        ├── nginx.conf
        ├── package.json
        ├── src/
        └── README.md
```

For detailed backend API documentation, see [Backend README](backend/README.md).

For frontend development instructions, see [Frontend README](FrontEnd/React/README.md).

## Important Notes

- This Docker configuration is intended for **local development and testing**, not public production deployment.
- The current administrator management endpoints are Development-only and are not yet protected by completed role-based authentication.
- The Order functionality is not included in the current application.
- The initial Docker build requires an internet connection.
- Each developer should create their own private `.env` file.
- Port `5173` serves the React frontend; port `5020` exposes the backend API.
- After changing the React source code, rebuild the frontend container to include the updated production build.