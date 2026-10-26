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
| Order availability validation | **Service implemented; no HTTP order API** | Backend validation logic exists, but frontend order submission is not connected yet. |
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
│   ├── Services/                # Menu and order validation logic
│   ├── Seeding/                 # Development sample data
│   ├── Migrations/              # Database schema changes
│   ├── wwwroot/images/          # Static and uploaded dish photos
│   └── Program.cs               # Startup, DI, CORS, Swagger, routes
└── Restaurant.Api.Tests/        # Automated tests
```

## 4. Quick Start with Docker

Docker Compose runs both the API and PostgreSQL. **Run the following commands from the repository root**, where `compose.yaml` is located.

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
| `GET` | `/health` | Basic API health check. | `200` |
| `GET` | `/health/db` | Verify that PostgreSQL is reachable. | `200`, `503` |

**Access and visibility:** `GET /dishes` is the customer-facing menu. The dish-detail, create, update, and image-upload endpoints are **Development-only**. They are **not authenticated Administrator endpoints** yet.

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

## 8. Frontend Integration Guidelines

In the React/Vite frontend, set the API address in `frontend/.env`:

```dotenv
VITE_API_BASE_URL=http://localhost:5020
```

The backend currently allows the origin **`http://localhost:5173`** by default. If the frontend uses another origin, the backend's `Cors:AllowedOrigins` configuration must be updated.

**Fetch the menu:**

```javascript
const API_BASE = import.meta.env.VITE_API_BASE_URL;

export async function getMenu() {
  const response = await fetch(`${API_BASE}/dishes`);
  if (!response.ok) throw new Error(`Menu request failed (${response.status})`);
  return response.json();
}
```

**Display the correct image (or a fallback):**

```javascript
export function dishImageSrc(dish) {
  return dish.imageUrl
    ? new URL(dish.imageUrl, import.meta.env.VITE_API_BASE_URL).toString()
    : "/placeholder-dish.jpg";
}
```

Put a real fallback image at `frontend/public/placeholder-dish.jpg`; also handle failed image loads in the component.

**Upload or replace a photo:**

```javascript
export async function uploadDishImage(dishId, imageFile) {
  const form = new FormData();
  form.append("image", imageFile);

  const response = await fetch(`${API_BASE}/dishes/${dishId}/image`, {
    method: "POST",
    body: form
  });

  if (!response.ok) throw new Error(`Image upload failed (${response.status})`);
  return response.json();
}
```

Do **not** set `Content-Type` manually for a `FormData` request; the browser supplies the multipart boundary.

**Recommended UI behavior:** handle loading, empty-menu, validation, request-error, and retry states. Refetch the dish/menu after a successful update or image upload. Do not display an order as submitted until a real order-creation API exists.

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
- **There is no completed `POST /orders` endpoint.** Internal order-availability validation exists, but orders are not accepted/persisted through an HTTP API yet.
- **There is no all-dishes admin-list endpoint.** `GET /dishes` hides unavailable items; an admin management screen will need a separate, properly protected listing endpoint.
- **There is no single-request dish + photo creation.** Save dish information first, then upload its image.
- **Photo upload is for controlled development use.** File size and signature checks exist, but production hardening (full image decoding/re-encoding, authentication, and operational safeguards) is still needed.
- **Reservation, kitchen and other restaurant workflows are not documented as available HTTP APIs** because the current controllers do not expose them.

For the exact runtime behavior, refer to the API's Swagger UI and the source under `Restaurant.Api/Controllers/`.
