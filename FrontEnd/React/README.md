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

The app is available at `http://localhost:5173`. Its API client uses `VITE_API_BASE_URL`; the example points to the ASP.NET development profile at `http://localhost:5019`. Set this variable to `http://localhost:5020` when using the API port published by Docker Compose.

The starter includes Home, Orders, Kitchen, Menu, and Reservations routes with placeholder pages. The Home page sends `GET /health` through the shared API client as a basic connectivity check.

Run `npm run build` to type-check and build the production frontend.
