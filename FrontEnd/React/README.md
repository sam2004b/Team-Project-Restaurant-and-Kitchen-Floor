# Frontend starter

## Requirements

- Node.js 20.19+ or 22.12+

## Install and run

```powershell
cd FrontEnd\React
npm install
Copy-Item .env.example .env
npm run dev
```

The app is available at `http://localhost:5173`. Its API client uses `VITE_API_BASE_URL`; the example points to the API port published by Docker Compose at `http://localhost:5020`. For the ASP.NET development profile running directly on the host, set it to `http://localhost:5019` instead. Restart Vite after changing the value.


The customer-facing menu displays available dishes retrieved from
the backend API, including their names, descriptions, prices,
categories, ingredients, and images.

The menu supports category filtering, loading and error states,
and manual refreshing to retrieve the latest dish availability.

Dish creation, editing, and image management are available
through the administrator interface.

Dish management is available at `http://localhost:5173/admin/dishes`. The add/edit forms validate dish fields, display server validation errors beside the corresponding fields, and let staff mark a dish as available or stop-listed. Successful saves show a confirmation and reload the dish list from the API, so saved changes remain visible after a page refresh. The management endpoints are enabled only in the ASP.NET Development environment until administrator authentication is configured.

Run `npm run build` to type-check and build the production frontend.
