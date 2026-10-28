# Restaurant Floor & Kitchen Management — Backend API

> **Integration guide · Development environment**  
> This document describes the backend functionality currently available to the frontend. It covers how to start the API, what each endpoint does, how to handle dish images, and which features are not ready yet.

## Table of Contents

1. [Overview](#1-overview)
2. [Implemented Features](#2-implemented-features)
3. [Technology and Project Structure](#3-technology-and-project-structure)
4. [Quick Start with Docker](#4-quick-start-with-docker)
5. [API Endpoint Reference](#5-api-endpoint-reference)
6. [Dish Data and API Examples](#6-dish-data-and-api-examples)
7. [Dish Images and Uploads](#7-dish-images-and-uploads)
8. [Frontend Integration Guidelines](#8-frontend-integration-guidelines)
9. [Errors and Validation](#9-errors-and-validation)
10. [Testing and Troubleshooting](#10-testing-and-troubleshooting)
11. [Current Limitations](#11-current-limitations)

---

## 1. Overview

The backend is an **ASP.NET Core 8 Web API** that provides data and business logic for the Restaurant Floor & Kitchen Management System. It stores restaurant data in **PostgreSQL** through **Entity Framework Core** and exposes HTTP endpoints consumed by the frontend.

**Communication:** `React frontend → HTTP/JSON API → PostgreSQL`. Images are served over HTTP; the frontend does not read files or connect to the database directly.

For local development with Docker Compose:

| Service | Address |
| --- | --- |
| API | `http://localhost:5020` |
| Swagger UI (Development only) | `http://localhost:5020/swagger` |
| Database health check | `http://localhost:5020/health/db` |
| PostgreSQL, from the host | `localhost:5435` |

> `localhost` means the computer running Docker. A frontend running on a different device must use the backend host's reachable address, with matching network and CORS configuration.

## 2. Implemented Features

| Feature | Status | What the frontend can expect |
| --- | --- | --- |
| Electronic menu | **Available** | Retrieve currently available dishes, with details, ingredients, and image URLs. |
| Dish availability | **Available** | Unavailable dishes are excluded from the public menu; availability can be changed by editing a dish in Development. |
| Dish details | **Available (Development)** | Retrieve an individual dish, including one that is unavailable. |
| Add and edit dishes | **Available (Development)** | Create a dish or update its information and ingredients. |
| Dish photo upload/replacement | **Available (Development)** | Upload a JPEG/PNG photo for an existing dish; the API saves its URL. |
| Sample data | **Available** | A development seeder adds example dishes, images, ingredients, roles, and accounts. |
| Administrator login / role-protected API | **Not implemented** | Identity roles exist in the database, but management endpoints are not secured by an Administrator login. |
| Reservations and kitchen workflows | **Not exposed as completed APIs** | Do not assume these endpoints exist. |

## 3. Technology and Project Structure

**Backend stack:** C# · .NET 8 · ASP.NET Core Web API · Entity Framework Core 8 · PostgreSQL · ASP.NET Core Identity · Swagger (Swashbuckle) · Docker Compose · xUnit.

```text
backend/
├── README.md                    # This integration guide
├── RestaurantManagement.sln     # Backend solution
├── Restaurant.Api/
│   ├── Controllers/             # HTTP endpoints
│   ├── Dtos/                    # API request/response models
│   ├── Models/                  # Database entities
│   ├── Data/                    # EF Core database context
│   ├── Services/                # Menu
│   ├── Seeding/                 # Development sample data
│   ├── Migrations/              # Database schema changes
│   ├── wwwroot/images/          # Static and uploaded dish photos
│   └── Program.cs               # Startup, DI, CORS, Swagger, routes
└── Restaurant.Api.Tests/        # Automated tests
```

## 4. Quick Start with Docker

Docker Compose runs frontend, API and PostgreSQL. **Run the following commands from the repository root**, where `compose.yaml` is located.

**1. Create local environment settings.**

Windows PowerShell:

```powershell
Copy-Item .env.example .env
```

macOS/Linux:

```bash
cp .env.example .env
```

In `.env`, configure `DB_NAME`, `DB_USER`, `DB_PASSWORD`, and `SEED_PASSWORD`. Use strong private passwords; **do not commit `.env`**. The `SEED_PASSWORD` must meet the Identity password rules (at least 10 characters, with uppercase, lowercase, a digit, and a symbol).

**2. Build and start the services.**

```bash
docker compose up --build -d
docker compose ps
```

The API container runs in **Development**, and pending EF Core migrations are applied at startup.

**3. Populate a new local database with sample data.**

```bash
docker compose run --rm api --seed
```

Seeding is designed to be repeatable without creating duplicate sample records. It adds **13 demonstration dishes** (12 initially available and one unavailable), along with development roles/accounts. Some sample dishes have empty ingredient lists.

**4. Verify that the API is ready.**

Open `http://localhost:5020/health/db`. A successful response is:

```json
{ "database": "connected" }
```

Then open `http://localhost:5020/swagger` to inspect and try the endpoints.

Useful commands:

```bash
docker compose logs --tail=100 api   # Diagnose API errors
docker compose down                  # Stop containers, keep data
dotnet test backend/RestaurantManagement.sln
```

**Do not use `docker compose down -v`** unless you intentionally want to remove the database and image-upload volumes.

## 5. API Endpoint Reference

All routes below are relative to **`http://localhost:5020`**.

| Method | Route | Description | Main responses |
| --- | --- | --- | --- |
| `GET` | `/dishes` | Return available menu dishes, ordered by name. | `200` |
| `GET` | `/dishes?limit=10` | Return up to the requested number of dishes (`1–100`). | `200`, `400` |
| `GET` | `/dishes/{id}` | Return one dish, including ingredients and image URL. | `200`, `400`, `403`, `404` |
| `POST` | `/dishes` | Create a dish from a JSON request. | `201`, `400`, `403` |
| `PUT` | `/dishes/{id}` | Update dish details and synchronize ingredients. | `200`, `400`, `403`, `404` |
| `POST` | `/dishes/{id}/image` | Upload or replace an existing dish's photo. | `200`, `400`, `403`, `404`, `413` |
| GET | /dishes/manage | Return all dishes for admin management,including unavailable dishes. | `200`, `403` |
| `GET` | `/health` | Basic API health check. | `200` |
| `GET` | `/health/db` | Verify that PostgreSQL is reachable. | `200`, `503` |

**Access and visibility:** `GET /dishes` and the customer order endpoints are available to the customer-facing app. The dish-detail, create, update, and image-upload endpoints are **Development-only**. They are **not authenticated Administrator endpoints** yet.

**Important behavior of `GET /dishes`:**

- Only records with `isAvailable: true` are included.
- Dishes are sorted by name, then ID.
- The response is `200 OK` with `[]` if no dishes are available.
- An invalid `limit` value returns `400 Bad Request`.
- It is **not** an admin list of every dish because unavailable dishes are hidden.

## 6. Dish Data and API Examples

### 6.1. Read the customer menu

**Request:** `GET /dishes`

**Example response (HTTP 200):**

```json
[
  {
    "id": 5,
    "name": "Carbonara",
    "price": 14.50,
    "description": "Sample Carbonara dish",
    "category": "Pasta",
    "isAvailable": true,
    "ingredients": ["Spaghetti", "Eggs", "Pecorino Romano", "Guanciale"],
    "imageUrl": "/images/dishes/carbonara.jpg"
  }
]
```

The example illustrates the response shape; IDs and current database values can vary. `description` and `imageUrl` may be `null`. `price` is numeric; **the backend does not specify a currency**.

### 6.2. Create a dish

**Request:** `POST /dishes` with `Content-Type: application/json`

```json
{
  "name": "Vegetable Pasta",
  "price": 12.50,
  "description": "Pasta with fresh vegetables",
  "category": "Pasta",
  "isAvailable": true,
  "ingredients": ["Pasta", "Tomato", "Basil"]
}
```

**Result:** `201 Created`, containing the newly created dish, including its generated `id`. A new dish's `imageUrl` is initially `null` until an image is uploaded.

### 6.3. Edit a dish

**Request:** `PUT /dishes/{id}` with the **full updated JSON object** in the same format as the create request.

**Result:** `200 OK`, containing the updated dish. The submitted ingredient list is synchronized with the saved list: removed names are deleted, and newly added names are saved. If no image is uploaded, the existing image is retained.

To make a dish unavailable, send `"isAvailable": false`. It will then disappear from the customer-facing `GET /dishes` response.


### 6.4. End-to-End Dish Management Integration

The React frontend is integrated with the ASP.NET Core API
and PostgreSQL.

#### Create a dish

Endpoint: `POST /dishes`

Example request:

```json
{
  "name": "Carbonara Integration Test",
  "price": 450,
  "description": "Traditional Italian pasta",
  "category": "Pasta",
  "isAvailable": true,
  "ingredients": [
    "Spaghetti",
    "Eggs",
    "Pecorino Romano",
    "Guanciale"
  ]
}
```

Expected response: `201 Created`, including the generated dish ID.

#### Edit a dish

Endpoint: `PUT /dishes/{id}`

Example request:

```json
{
  "name": "Carbonara Integration Test",
  "price": 500,
  "description": "Traditional Italian pasta",
  "category": "Pasta",
  "isAvailable": true,
  "ingredients": [
    "Spaghetti",
    "Egg Yolks",
    "Pecorino Romano",
    "Guanciale"
  ]
}
```

Expected response: `200 OK`, containing the updated dish.

#### Verify integration

1. Start the full application with Docker Compose.
2. Create a dish through the React administrator interface.
3. Verify that PostgreSQL stores the dish and its ingredients.
4. Confirm that the customer menu displays the new dish.
5. Edit the dish and verify the updated price and ingredients.
6. Mark the dish unavailable and verify it is excluded from
   the customer menu API.
7. Restore availability and confirm it appears again.

The frontend sends JSON requests to the backend through the
Nginx `/api` reverse proxy. The backend saves changes using
Entity Framework Core and PostgreSQL.

The administrator interface is available at:
`http://localhost:5173/admin/dishes`

The customer menu is available at:
`http://localhost:5173`

Dish management endpoints are currently Development-only
and do not yet implement administrator authentication.


## 7. Dish Images and Uploads

The backend stores each dish's **relative image path** in `imageUrl`, not the image bytes in PostgreSQL.

| Image type | URL pattern | Storage |
| --- | --- | --- |
| Seeded sample image | `/images/dishes/carbonara.jpg` | Static files included with the API |
| Uploaded/replaced image | `/images/uploads/{generated-file-name}.jpg` | Persistent Docker `dish_uploads` volume |

The current `compose.yaml` mounts `dish_uploads` at `/app/wwwroot/images/uploads`, so uploaded files are preserved across ordinary container recreation (unless the volume is removed).

**Upload request:** `POST /dishes/{id}/image`

- Body type: **`multipart/form-data`**.
- Form field name: **`image`**.
- File types: **JPEG or PNG**.
- Maximum file size: **5 MB**.
- The dish must already exist.

**Example successful response (HTTP 200):**

```json
{
  "dishId": 14,
  "imageUrl": "/images/uploads/8c5f1c7453ef4d07a86dc6348ae3a718.jpg"
}
```

Uploading a new photo replaces the existing `imageUrl`. A previous user-uploaded file may be deleted; original seeded image files are preserved.

**Recommended form flow:** Create or update the dish using JSON first. Once the dish has an ID, upload the photo in a separate multipart request. If the photo upload fails, the dish data may already be saved: show a retry option instead of creating the dish again.

## 8. Frontend Integration

The React frontend communicates with the ASP.NET Core backend through REST API endpoints.

The backend is responsible for retrieving, creating, updating, and storing restaurant data in PostgreSQL. The frontend is responsible for displaying this information and allowing users to interact with it.

### 8.1. Frontend Project Location

The React application is located at:

```text
FrontEnd/React/
```

Important frontend files:

| File | Purpose |
|---|---|
| `src/App.tsx` | Displays the customer menu, including dishes, ingredients, prices, and images |
| `src/AdminDishesPage.tsx` | Provides the administrator interface for creating and editing dishes |
| `src/services/api.ts` | Contains functions for communicating with the backend API |
| `Dockerfile` | Builds the React application inside Docker |
| `nginx.conf` | Serves React and forwards API requests to the backend |

### 8.2. Running Frontend and Backend with Docker

The project uses Docker Compose to run three services:

| Service | Technology | Port |
|---|---|---|
| Frontend | React + Nginx | 5173 |
| Backend | ASP.NET Core 8 | 5020 |
| Database | PostgreSQL | 5435 |

From the repository root, execute:

```bash
docker compose up --build -d
```

Once all containers are running, open:

- **Customer menu:** http://localhost:5173
- **Administrator interface:** http://localhost:5173/admin/dishes
- **Swagger API documentation:** http://localhost:5020/swagger

The frontend, backend, and database communicate through Docker's internal network.

### 8.3. API Communication Through Nginx

When running with Docker, the frontend uses the following environment variable:

```env
VITE_API_BASE_URL=/api
```

This value is configured during the frontend Docker build.

Nginx forwards requests beginning with `/api/` to the ASP.NET Core backend.

For example:

```text
React application
    |
    | GET /api/dishes
    v
Nginx (frontend container)
    |
    | GET http://api:8080/dishes
    v
ASP.NET Core backend
    |
    v
PostgreSQL database
```

This configuration allows the frontend to communicate with the backend without hard-coded machine-specific IP addresses.

**Important:** The `/api` prefix is used by Nginx. The backend endpoints themselves remain `/dishes`, `/dishes/manage`, and so on.

### 8.4. Running React Separately During Development

Developers who prefer to run React outside Docker can use Vite.

Create a `.env` file inside `FrontEnd/React/` containing:

```env
VITE_API_BASE_URL=http://localhost:5020
```

Then run:

```bash
cd FrontEnd/React
npm install
npm run dev
```

The backend must already be running and accessible on port `5020`.

The Docker configuration does not require this local React `.env` file.

### 8.5. API Endpoints Used by React

The frontend uses the following endpoints:

| Method | Endpoint | Frontend Usage |
|---|---|---|
| `GET` | `/dishes` | Retrieve dishes available to customers |
| `GET` | `/dishes/manage` | Retrieve all dishes for administrator management |
| `GET` | `/dishes/{id}` | Retrieve information about one dish |
| `POST` | `/dishes` | Create a new dish |
| `PUT` | `/dishes/{id}` | Update an existing dish |
| `POST` | `/dishes/{id}/image` | Upload or replace a dish photograph |

The corresponding frontend API functions are defined in:

`FrontEnd/React/src/services/api.ts`

### 8.6. Creating and Editing Dishes

**Creating a dish**

1. The administrator completes the Add Dish form.
2. React sends a `POST /dishes` request containing the dish information and ingredient list.
3. The backend validates the request and saves the dish in PostgreSQL.
4. The backend returns the created dish, including its generated ID.
5. If a photograph was selected, React uploads it through `POST /dishes/{id}/image`.
6. The new dish appears in the customer menu when available.

**Editing a dish**

1. The administrator selects an existing dish.
2. React retrieves its current information.
3. The administrator updates the name, price, description, category, ingredients, or availability.
4. React sends a `PUT /dishes/{id}` request.
5. The backend updates the existing database record and its ingredients.
6. The customer menu displays the updated information after refreshing.

Dish images are uploaded separately from the JSON request.

### 8.7. Displaying Dish Images

The backend returns the `imageUrl` property for each dish.

Example:

```json
{
  "id": 1,
  "name": "Margherita Pizza",
  "price": 450,
  "ingredients": ["Tomato", "Mozzarella", "Basil"],
  "imageUrl": "/images/dishes/margherita-pizza.jpg"
}
```

The example values are illustrative.

When using Docker, the frontend combines the `/api` prefix with the relative image path:

```text
/api/images/dishes/margherita-pizza.jpg
```

Nginx forwards this request to the backend, which serves the photograph.

The frontend should display a placeholder if `imageUrl` is null or the photograph cannot be loaded.

### 8.8. Important Integration Notes

- The customer menu (`GET /dishes`) returns only available dishes.
- The administrator list (`GET /dishes/manage`) includes unavailable dishes.
- Ingredients are returned as an array of strings.
- React should refresh the customer menu after dish information changes.
- Image uploads use `multipart/form-data` with a file field named `image`.
- Docker stores uploaded photographs in a persistent volume.
- The Order functionality has been removed from the current application.
- Administrator authentication and role-based authorization are not yet implemented. Dish management is currently intended for the Development environment only.

For complete request and response schemas, use Swagger at http://localhost:5020/swagger.

## 9. Errors and Validation

| HTTP status | Meaning | Frontend action |
| --- | --- | --- |
| `200 OK` | Successful read, update, or upload | Use returned data. |
| `201 Created` | Dish created | Read returned ID; optionally upload its photo. |
| `400 Bad Request` | Invalid parameters, dish data, or file | Display relevant validation feedback. |
| `403 Forbidden` | Management endpoint is disabled outside Development | Do not assume this represents an authenticated user's role. |
| `404 Not Found` | Dish ID does not exist | Display not-found feedback. |
| `413 Payload Too Large` | Uploaded image exceeds accepted size | Ask for an image under 5 MB. |
| `503 Service Unavailable` | Database health check failed | Check PostgreSQL and the API logs. |

**Dish validation currently checks:** required nonblank name (maximum 150 characters), nonblank category (maximum 80 characters), description length (maximum 1,000 characters), positive price within the database range with at most two decimal places, and valid ingredient names (nonblank, maximum 150 characters, case-insensitively unique). The `ingredients` field must be an array, not `null`.

Error bodies are **not all identical**. Some responses use `{ "error": "..." }`; ASP.NET Core model validation can return an object with `title` and `errors`. Handle both, and provide a useful fallback message for non-JSON responses or network failures.

## 10. Testing and Troubleshooting

**Build and run automated tests** (from the repository root):

```bash
dotnet build backend/RestaurantManagement.sln
dotnet test backend/RestaurantManagement.sln
```

| Problem | First thing to check |
| --- | --- |
| Frontend cannot reach API | `docker compose ps` and `VITE_API_BASE_URL` |
| Browser reports a CORS error | Frontend origin versus backend `Cors:AllowedOrigins` |
| Menu returns `[]` | Seed data and dish availability; unavailable dishes are filtered |
| Image is broken | Combine API base URL with `imageUrl`; verify the file exists; show a fallback |
| Swagger returns HTTP 500 | `docker compose logs --tail=100 api` |
| Database health check fails | `.env`, database container status, and API logs |
| Seeder reports missing password | Configure `SEED_PASSWORD` in `.env` |

Swagger is available at **`http://localhost:5020/swagger`** when the backend runs in Development. Use it to inspect schemas and test the implemented routes.

## 11. Current Limitations

This is a **development API**, not a finished production backend:

- **Administrator authentication and authorization are not implemented.** The database contains Identity roles and development accounts, but sensitive dish-management routes are only blocked outside Development; they do not verify the caller's Administrator role.
-  `GET /dishes` hides unavailable items; an admin management screen will need a separate, properly protected listing endpoint.
- **There is no single-request dish + photo creation.** Save dish information first, then upload its image.
- **Photo upload is for controlled development use.** File size and signature checks exist, but production hardening (full image decoding/re-encoding, authentication, and operational safeguards) is still needed.
- **Reservation, kitchen and other restaurant workflows are not documented as available HTTP APIs** because the current controllers do not expose them.

For the exact runtime behavior, refer to the API's Swagger UI and the source under `Restaurant.Api/Controllers/`.
