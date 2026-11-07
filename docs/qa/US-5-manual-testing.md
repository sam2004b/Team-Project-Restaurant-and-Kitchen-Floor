# US-5 — QA Testing

**Date** — 06/11/2026<br>
**Tester** — Shaik<br>
**Environment** — Local ASP.NET Core 8 API (`http://localhost:15020`), isolated PostgreSQL 16 database (`localhost:25435`), and Chrome against the React frontend (`http://localhost:15173/admin/staff`)<br>
**Branch** — `origin/develop`<br>
**Commit** — `0833a720dfcc62c42d5a9e962d0ff8aede5221e1`

## Scope

Restaurant account creation, required-name validation, duplicate prevention, saved-profile retrieval, and tenant isolation.

Testing used the isolated Docker Compose project `us5qa0833a72` with dedicated `us5qa0833a72_postgres_data` and `us5qa0833a72_dish_uploads` volumes. Existing local restaurant data and repository changes were not used or modified. Credentials and bearer tokens are redacted from this report.

The latest public US-5 implementation material covers required fields, duplicate-name prevention, login, restaurant creation, and cross-restaurant staff authorization. The profile retrieval and cross-restaurant profile checks use the integrated `/restaurants/{restaurantId}/profile` route added by the later restaurant-configuration work. All four requested cases were executed against the integrated Sprint 2 build.

## Tests

- **US5-01** — Admin created a restaurant and reopened its saved profile — **PASS**
  - `POST /auth/login` returned `200`; the access token is redacted.
  - `POST /restaurants` returned `201`:

    ```json
    {
      "id": 3,
      "name": "US5 QA Alpha 0833a72",
      "address": "101 Isolated QA Avenue"
    }
    ```

  - A second login scoped to restaurant `3` returned `200`.
  - `GET /restaurants/3/profile` returned `200` with the same ID, name, and address.
  - A read-only PostgreSQL query found exactly one row matching the submitted name and address.

- **US5-02** — An empty name was rejected with a field-specific message and no restaurant was created — **PASS**
  - `POST /restaurants` with an empty `name` returned `400`.
  - The response identified the field:

    ```json
    {
      "status": 400,
      "errors": {
        "Name": [
          "The Name field is required."
        ]
      }
    }
    ```

  - Read-only database checks found zero rows with the request's unique address and zero restaurants with a blank name.

- **US5-03** — A duplicate restaurant request was rejected and exactly one matching account remained — **PASS**
  - Resubmitting the exact US5-01 name returned `409`:

    ```json
    {
      "error": "Restaurant name already exists."
    }
    ```

  - A read-only database query confirmed that exactly one restaurant with the US5-01 name remained.

- **US5-04** — Bistro displayed its own data, and direct Cafe profile and staff reads were denied using a Bistro-scoped token — **PASS**
  - A second restaurant was created with `201`:

    ```json
    {
      "id": 5,
      "name": "US5 QA Beta 0833a72",
      "address": "202 Tenant Boundary Road"
    }
    ```

  - Login scoped to restaurant `5` returned `200`, and its own profile returned `200` with the saved values.
  - Login scoped to restaurant `3` returned `200`.
  - `GET /auth/me` returned `200` and confirmed restaurant ID `3` with the `Administrator` role. Account identifiers and the token are redacted.
  - With the restaurant `3` token:
    - `GET /restaurants/5/profile` returned `403` with an empty body.
    - `GET /restaurants/5/staff` returned `403` with an empty body.
    - Neither response contained restaurant `5` data.
  - Read-only database checks showed one active Administrator membership for restaurant `3` and one for restaurant `5`.
  - In Chrome, the restaurant `3` profile form displayed `US5 QA Alpha 0833a72` and `101 Isolated QA Avenue`. The restaurant `5` name was not displayed.

## Result

**4 PASS, 0 FAIL, 0 BLOCKED, 0 NOT RUN.**

- **Manual Swagger evidence** — [Local Swagger UI](http://localhost:15020/swagger). The OpenAPI document returned `200`, and the observed endpoint responses are recorded in the test cases above. Interactive Swagger browser control was unavailable during the run, so requests were sent directly to the same mapped API endpoints.
- **Automated API coverage and CI** — The .NET 8 test suite passed **66/66**. Relevant partial coverage exists in [`StaffRoleAuthorizationTests.cs`](../../backend/Restaurant.Api.Tests/StaffRoleAuthorizationTests.cs), [`StaffSessionValidatorTests.cs`](../../backend/Restaurant.Api.Tests/StaffSessionValidatorTests.cs), and [`RestaurantSettingsPersistenceTests.cs`](../../backend/Restaurant.Api.Tests/RestaurantSettingsPersistenceTests.cs). The exact commit passed both the [backend and frontend CI jobs](https://github.com/sam2004b/Team-Project-Restaurant-and-Kitchen-Floor/actions/runs/42217997821).
- **Independent browser-to-database verification** — Shaik — **PASS**. The frontend showed the first restaurant's saved profile, while read-only PostgreSQL queries verified the expected row counts, active memberships, and absence of a record from the empty-name request.
- **Defects or blockers** — None observed for the four test cases. Automated controller-level tests for restaurant creation, validation, duplicate handling, cross-restaurant profile/staff denial, and frontend profile rendering are not currently present; this is a coverage gap rather than a failure in the tested behavior.
