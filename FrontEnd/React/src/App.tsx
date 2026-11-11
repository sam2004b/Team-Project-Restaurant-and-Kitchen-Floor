import { useMenuUpdates } from "./services/useMenuUpdates";
import { useEffect, useMemo, useState } from "react";
import {
  apiBaseUrl,
  clearStoredAccessToken,
  getCurrentAccount,
  getMenuDishes,
  getStoredAccessToken,
  type AccountResponse,
} from "./services/api";
import { Link, Navigate, NavLink, Outlet, Route, Routes, useLocation, useNavigate } from "react-router-dom";
import AdminDishesPage from "./AdminDishesPage";
import { AcceptInvitationPage, StaffLoginPage, StaffManagementPage } from "./StaffAccessPage";
import { WaiterTablesPage } from "./RestaurantConfigurationPanel";
import RestaurantSettingsPage from "./RestaurantSettingsPage";
import ChefAvailabilityPage from "./ChefAvailabilityPage";
import { formatPrice } from "./services/currency";
import WaiterOrderPage from "./WaiterOrderPage";
import KitchenOrdersPage from "./KitchenOrdersPage";


type MenuDish = {
  id: number;
  name: string;
  description: string;
  category: string;
  price: number;
  imageUrl: string | null;
    isAvailable: boolean;
    ingredients: string[];
};

const categoryOptions = ["All", "Pasta", "Pizza", "Soups", "Salads", "Mains", "Desserts", "Drinks"] as const;

const normalizeCategoryName = (category: string) => {
  const normalized = category.trim();

  switch (normalized.toLowerCase()) {
    case "pasta":
      return "Pasta";
    case "pizza":
      return "Pizza";
    case "soup":
    case "soups":
      return "Soups";
    case "salad":
    case "salads":
      return "Salads";
    case "main":
    case "mains":
      return "Mains";
    case "dessert":
    case "desserts":
      return "Desserts";
    case "drink":
    case "drinks":
      return "Drinks";
    default:
      return normalized || "Other";
  }
};

type DishArt =
  | "pasta"
  | "soup"
  | "main"
  | "salad"
  | "pizza"
  | "dessert"
  | "starter";

const dishArtByName: Record<string, DishArt> = {
  Carbonara: "pasta",
  "Tomato Soup": "soup",
  "Grilled Chicken": "main",
  "House Salad": "salad",
  "Margherita Pizza": "pizza",
  "Grilled Salmon": "main",
  "Mushroom Risotto": "main",
  "Caesar Salad": "salad",
  Cheesecake: "dessert",
  "Garlic Bread": "starter",
  "Beef Steak": "main",
  "Lemon Tart": "dessert",
};

function MenuPage() {
  const [dishes, setDishes] = useState<MenuDish[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [selectedCategory, setSelectedCategory] = useState<(typeof categoryOptions)[number]>("All");
  const [recentlyUnavailable, setRecentlyUnavailable] = useState<MenuDish | null>(null);

  const loadMenu = async (showLoading = true): Promise<boolean> => {
    if (showLoading) {
      setLoading(true);
    }
    setError(null);

    try {
      const response = await getMenuDishes();
      const availableDishes = response
        .filter((dish) => dish.isAvailable)
        .map((dish) => ({
          id: dish.id,
          name: dish.name,
          description: dish.description ?? "Chef's special",
          category: dish.category,
          price: dish.price,
          imageUrl: dish.imageUrl,
            isAvailable: true,
            ingredients: dish.ingredients ?? [],
        }));
      const availableIds = new Set(availableDishes.map((dish) => dish.id));
      const dishThatBecameUnavailable = dishes.find((dish) => !availableIds.has(dish.id));

      setDishes(availableDishes);
      setRecentlyUnavailable(
        dishThatBecameUnavailable
          ? { ...dishThatBecameUnavailable, isAvailable: false }
          : null,
      );
      return true;
    } catch (caughtError) {
      setError(
        caughtError instanceof Error
          ? caughtError.message
          : "An unexpected error occurred while loading the menu.",
      );
      return false;
    } finally {
      if (showLoading) {
        setLoading(false);
      }
    }
  };

  useEffect(() => {
    void loadMenu();
  }, []);

  useMenuUpdates(() => {
    void loadMenu(false);
  });

  const visibleDishes = useMemo(() => {
    if (selectedCategory === "All") {
      return dishes;
    }

    return dishes.filter((dish) => normalizeCategoryName(dish.category) === selectedCategory);
  }, [dishes, selectedCategory]);

  const displayDishes = visibleDishes;

  

  return (
    <div className="app-shell">
      <main className="page">
        <section className="hero-panel">
          <div className="hero-copy">
            <p className="eyebrow">WELCOME TO</p>
            <h1>PLATE Restaurant</h1>
            <p className="subtitle">Explore our menu by category</p>

          </div>
        </section>

        <section className="category-strip" aria-label="Dish categories">
          <span className="category-label">CATEGORIES</span>
          {categoryOptions.map((category) => (
            <button
              key={category}
              type="button"
              className={`category-pill ${selectedCategory === category ? "active" : ""}`}
              onClick={() => setSelectedCategory(category)}
            >
              {category}
            </button>
          ))}
        </section>

        <section className="menu-header">
          <h2>Available dishes</h2>
        </section>

        {loading ? (
          <div className="loading-grid" aria-live="polite" aria-busy="true">
            {Array.from({ length: 6 }).map((_, index) => (
              <div key={index} className="card skeleton-card">
                <div className="skeleton-art" />
                <div className="skeleton-line narrow" />
                <div className="skeleton-line" />
                <div className="skeleton-row">
                  <div className="skeleton-line price" />
                  <div className="skeleton-button" />
                </div>
              </div>
            ))}
          </div>
        ) : error && dishes.length === 0 ? (
          <div className="error-panel">
            <div className="error-badge">!</div>
            <h3>Could not load menu</h3>
            <p>{error}</p>
            <button type="button" className="primary-button" onClick={() => void loadMenu()}>
              Try again
            </button>
          </div>
        ) : displayDishes.length === 0 ? (
          <div className="empty-panel" role="status">
            <div className="empty-badge" aria-hidden="true">~</div>
            <h3>No dishes available</h3>
            <p>There are currently no available dishes in this category. Please check back soon.</p>
            <button type="button" className="primary-button" onClick={() => setSelectedCategory("All")}>
              Show all dishes
            </button>
          </div>
        ) : (
          <>
            {recentlyUnavailable && (
              <div className="availability-notice" role="status">
                <strong>Availability changed</strong>
                <b>{recentlyUnavailable.name} is no longer available.</b>
                  <p>The menu has updated automatically. This dish is no longer available.</p>
              </div>
            )}
            {error && (
              <div className="refresh-error" role="alert">
                <span>Could not refresh the menu. Your last loaded menu is still shown. {error}</span>
                <button type="button" onClick={() => void loadMenu()}>Try again</button>
              </div>
            )}
            <div className="dish-grid">
              {displayDishes.map((dish) => (
                <article key={dish.id} className={`dish-card ${dish.isAvailable ? "" : "unavailable-card"}`}>
                  <div className={`dish-art food-${dishArtByName[dish.name] ?? "main"}`}>
                    <span className="dish-shadow" />
                    <span className="dish-plate" />
                    {dish.imageUrl && (
                      <img
                        className="dish-photo"
                        src={`${apiBaseUrl}${dish.imageUrl}`}
                        alt={dish.name}
                        onError={(event) => {
                          event.currentTarget.hidden = true;
                        }}
                      />
                    )}
                  </div>
                  <div className="dish-content">
                    <div className="dish-meta">
                      <div className="dish-category">{normalizeCategoryName(dish.category)}</div>
                      <h3>{dish.name}</h3>
                              <p>{dish.description}</p>
                              {dish.ingredients.length > 0 && (
                                  <p className="dish-ingredients">
                                      <strong>Ingredients:</strong>{" "}
                                      {dish.ingredients.join(", ")}
                                  </p>
                              )}
                    </div>
                    <div className="dish-footer">
                      <span className="price">{formatPrice(dish.price)}</span>
                      <span className={`availability-badge ${dish.isAvailable ? "available" : "unavailable"}`}>
                        <span className="availability-dot" aria-hidden="true" />
                        {dish.isAvailable ? "Available" : "Unavailable"}
                      </span>
                    </div>
                  </div>
                </article>
              ))}
            </div>
          </>
        )}

      </main>
    </div>
  );
}

type AllowedRole = "Waiter" | "Chef" | "Administrator" | "Reception Manager";

function normalizeRole(role?: string | null): AllowedRole | "Unknown" {
  const normalized = role?.trim();
  if (!normalized) return "Unknown";

  const key = normalized.toLowerCase();
  if (key === "waiter") return "Waiter";
  if (key === "chef") return "Chef";
  if (key === "administrator") return "Administrator";
  if (key === "reception manager") return "Reception Manager";
  return "Unknown";
}

function ProtectedRoute({ allowedRoles, children }: { allowedRoles: AllowedRole[]; children: React.ReactNode }) {
  const location = useLocation();
  const [account, setAccount] = useState<AccountResponse | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let active = true;

    const token = getStoredAccessToken();
    if (!token) {
      setAccount(null);
      setLoading(false);
      return () => {
        active = false;
      };
    }

    getCurrentAccount()
      .then((currentAccount) => {
        if (!active) return;
        setAccount(currentAccount);
        setLoading(false);
      })
      .catch(() => {
        if (!active) return;
        clearStoredAccessToken();
        setAccount(null);
        setLoading(false);
      });

    return () => {
      active = false;
    };
  }, [location.pathname]);

  if (loading) {
    return (
      <div className="auth-state-panel" role="status">
        <h2>Checking your session...</h2>
      </div>
    );
  }

  if (!account) {
    return <Navigate to="/login" replace state={{ from: location.pathname }} />;
  }

  const accountRole = normalizeRole(account.role);
  if (accountRole === "Unknown" || !allowedRoles.includes(accountRole)) {
    return (
      <div className="auth-state-panel" role="alert">
        <h2>Access denied</h2>
        <p>You do not have permission to view this page.</p>
        <button type="button" className="primary-button" onClick={() => window.location.assign("/login")}>
          Back to sign in
        </button>
      </div>
    );
  }

  return <>{children}</>;
}

function StaffWorkspaceLayout() {
  const navigate = useNavigate();
  const [account, setAccount] = useState<AccountResponse | null>(null);

  useEffect(() => {
    let active = true;
    if (!getStoredAccessToken()) {
      setAccount(null);
      return () => {
        active = false;
      };
    }

    void getCurrentAccount()
      .then((currentAccount) => {
        if (active) setAccount(currentAccount);
      })
      .catch(() => {
        if (active) setAccount(null);
      });

    return () => {
      active = false;
    };
  }, []);

  const role = normalizeRole(account?.role);
  const navigation = role === "Chef"
    ? [
      { label: "Kitchen orders", path: "/chef/orders" },
      { label: "Dish availability", path: "/chef/availability" },
    ]
    : role === "Waiter"
      ? [
        { label: "My tables", path: "/waiter/tables" },
        { label: "Create orders", path: "/waiter/orders" },
      ]
      : [
        { label: "Menu management", path: "/admin/dishes" },
        { label: "Staff and access", path: "/admin/staff" },
        { label: "Settings", path: "/admin/settings" },
      ];

  const signOut = () => {
    clearStoredAccessToken();
    navigate("/login", { replace: true });
  };

  return (
    <div className="staff-workspace-shell">
      <aside className="admin-sidebar staff-shared-sidebar">
        <Link className="admin-brand" to="/" aria-label="Plate restaurant customer menu">
          <strong>PLATE</strong>
          <span>RESTAURANT OPERATIONS</span>
        </Link>
        <div className="admin-sidebar-label">{role === "Unknown" ? "WORKSPACE" : `${role.toUpperCase()} WORKSPACE`}</div>
        {navigation.map((item) => (
          <NavLink
            key={item.path}
            className={({ isActive }) => `admin-sidebar-link${isActive ? " active" : ""}`}
            to={item.path}
          >
            {item.label}
          </NavLink>
        ))}
        <Link className="admin-sidebar-link" to="/">Customer menu</Link>
        <div className="admin-profile">
          <span className="admin-avatar" aria-hidden="true">
            {(account?.userName?.slice(0, 2) || "ST").toUpperCase()}
          </span>
          <span>
            <strong>{account?.userName || "Signed in"}</strong>
            <small>{account?.role || "Staff account"}</small>
          </span>
          <button type="button" className="staff-sidebar-signout" onClick={signOut}>Sign out</button>
        </div>
      </aside>
      <div className="staff-workspace-content">
        <Outlet />
      </div>
    </div>
  );
}

function App() {
  return (
    <Routes>
      <Route path="/" element={<MenuPage />} />
      <Route path="/login" element={<StaffLoginPage />} />
      <Route path="/menu" element={<MenuPage />} />
      <Route element={<StaffWorkspaceLayout />}>
        <Route path="/admin/dishes" element={<ProtectedRoute allowedRoles={["Administrator", "Reception Manager"]}><AdminDishesPage /></ProtectedRoute>} />
        <Route path="/admin/staff" element={<ProtectedRoute allowedRoles={["Administrator", "Reception Manager"]}><StaffManagementPage /></ProtectedRoute>} />
        <Route path="/admin/settings" element={<ProtectedRoute allowedRoles={["Administrator", "Reception Manager"]}><RestaurantSettingsPage /></ProtectedRoute>} />
        <Route path="/chef/availability" element={<ProtectedRoute allowedRoles={["Chef"]}><ChefAvailabilityPage /></ProtectedRoute>} />
        <Route path="/chef/menu" element={<ProtectedRoute allowedRoles={["Chef"]}><ChefAvailabilityPage /></ProtectedRoute>} />
        <Route path="/chef/orders" element={<ProtectedRoute allowedRoles={["Chef"]}><KitchenOrdersPage /></ProtectedRoute>} />
        <Route path="/waiter/orders" element={<ProtectedRoute allowedRoles={["Waiter"]}><WaiterOrderPage /></ProtectedRoute>} />
        <Route path="/waiter/tables" element={<ProtectedRoute allowedRoles={["Waiter"]}><WaiterTablesPage /></ProtectedRoute>} />
      </Route>
      <Route path="/invitations/accept" element={<AcceptInvitationPage />} />
      <Route path="*" element={<Navigate to="/login" replace />} />
    </Routes>
  );
}

export default App;
