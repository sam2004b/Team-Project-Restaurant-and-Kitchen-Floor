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

The menu shows only available dishes and submits orders to the API. If a dish becomes unavailable before an order is submitted, the menu refreshes and shows a clear message. The latest order confirmation is restored from the server after a browser refresh.

Dish management is available at `http://localhost:5173/admin/dishes`. It reads and writes menu data through the dish management API. The management endpoints are enabled only in the ASP.NET Development environment until administrator authentication is configured.

Run `npm run build` to type-check and build the production frontend.
