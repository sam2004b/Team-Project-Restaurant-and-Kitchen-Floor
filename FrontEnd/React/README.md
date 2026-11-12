# Frontend starter

## Quick Start with Docker

To run the complete application (frontend, backend, and PostgreSQL)
using Docker, follow the instructions in the
[main project README](../../README.md).

No local Node.js or .NET installation is required for Docker-based usage.

The instructions below are intended for developers who want
to run React separately with Vite.




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

The public menu is available to everyone at
[http://localhost:5173/](http://localhost:5173/). Staff sign in at
[http://localhost:5173/login](http://localhost:5173/login); after the API
confirms the account role, waiters go to their order workspace, chefs go to
kitchen orders, and administrators or reception managers go to staff access.
Staff-only routes redirect unauthenticated visitors to the sign-in page.

The menu supports category filtering, loading and error states,
and manual refreshing to retrieve the latest dish availability.

Dish creation, editing, and image management are available
through the administrator interface.

Dish management is available at
[http://localhost:5173/admin/dishes](http://localhost:5173/admin/dishes). The
add/edit forms validate dish fields and display server validation errors beside
the corresponding fields. Staff can also stop-list or re-enable a dish directly
from the list; the switch changes only after the backend confirms the update,
then the list refreshes. Failed or stale updates display the API error and
reload the current dish state. Availability changes are broadcast to connected
customer menus, and the backend prevents unavailable dishes from being selected
for new orders.

Account and staff access management is available at
[http://localhost:5173/admin/staff](http://localhost:5173/admin/staff). After
signing in, choose an existing
restaurant ID (or create one if the account has bootstrap permission), send
and revoke staff invitations, and change or refresh membership roles/status.
The selected restaurant profile can also be loaded and updated from this page;
profile changes require active administrator access.
The bearer token is kept in browser session storage for the current tab session.
After loading a restaurant, the staff workspace also loads and saves its default
seat-capacity setting and table configuration through the restaurant API. Table
number and seat-count inputs require positive whole numbers; server validation,
including duplicate-number conflicts, is shown without a success state.
Successful table and assignment changes reload server state. Waiters can view
their current assigned tables and seat counts at
[http://localhost:5173/waiter/tables](http://localhost:5173/waiter/tables).
The waiter order workspace includes a history of orders for currently assigned
tables. Selecting a saved open order lets the waiter continue editing it; sent
orders are read-only because the current API does not allow changes after send.
Chef accounts can open
[http://localhost:5173/chef/availability](http://localhost:5173/chef/availability)
or [http://localhost:5173/chef/menu](http://localhost:5173/chef/menu) to view available
dishes from the public menu API and change availability. Dishes stopped from
this browser are saved locally so they can be re-enabled here. Full dish-detail
editing remains restricted to administrators by the existing API.
Invitation links open
[http://localhost:5173/invitations/accept](http://localhost:5173/invitations/accept);
invited staff can accept with an
existing signed-in account or create a new account during acceptance. The API
does not currently expose a restaurant or invitation listing endpoint, so the
staff page loads memberships by restaurant ID and shows the latest invitation
result while the API reports duplicate, authorization, expiry, and delivery
errors.

Run `npm run build` to type-check and build the production frontend.
