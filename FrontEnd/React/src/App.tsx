import { useMenuUpdates } from "./services/useMenuUpdates";
import { useEffect, useMemo, useState } from "react";
import {
  apiBaseUrl,
  getMenuDishes,
} from "./services/api";
import { Navigate, Route, Routes, useLocation } from "react-router-dom";
import type { ReactNode } from "react";
import AdminDishesPage from "./AdminDishesPage";
import { AcceptInvitationPage, StaffManagementPage } from "./StaffAccessPage";
import RestaurantSettingsPage from "./RestaurantSettingsPage";
import ChefAvailabilityPage from "./ChefAvailabilityPage";
import { formatPrice } from "./services/currency";
import WaiterOrderPage from "./WaiterOrderPage";
import KitchenOrdersPage from "./KitchenOrdersPage";
import { useAuth } from "./services/AuthContext";


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

type StaffRole = "Administrator" | "Waiter" | "Chef";

function defaultWorkspace(role: string | undefined): string {
  if (role === "Waiter") return "/waiter/orders";
  if (role === "Chef") return "/chef/orders";
  return "/admin/staff";
}

function ProtectedRoute({
  allowedRoles,
  children,
}: {
  allowedRoles: StaffRole[];
  children: ReactNode;
}) {
  const { account, status, sessionMessage, refreshAccount } = useAuth();
  const location = useLocation();

  if (status === "loading") {
    return <div className="admin-loading" role="status" aria-busy="true">Checking your session...</div>;
  }

  if (status === "error") {
    return (
      <main className="page admin-page">
        <section className="staff-card" role="alert">
          <h1>Could not verify your session</h1>
          <p>Check your connection and try again.</p>
          <button type="button" className="primary-button" onClick={() => void refreshAccount()}>
            Try again
          </button>
        </section>
      </main>
    );
  }

  if (status !== "authenticated" || !account) {
    const signedOut = sessionMessage?.toLowerCase().includes("signed out") ?? false;
    return (
      <Navigate
        to="/admin/staff"
        replace
        state={{
          ...(!signedOut && {
            returnTo: `${location.pathname}${location.search}${location.hash}`,
          }),
          authMessage: sessionMessage ?? "Sign in to continue to that workspace.",
        }}
      />
    );
  }

  if (!allowedRoles.some((role) => role === account.role)) {
    return (
      <Navigate
        to={defaultWorkspace(account.role)}
        replace
        state={{ authMessage: `Your ${account.role ?? "current"} role cannot open that workspace.` }}
      />
    );
  }

  return children;
}

function protect(allowedRoles: StaffRole[], element: ReactNode) {
  return <ProtectedRoute allowedRoles={allowedRoles}>{element}</ProtectedRoute>;
}

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

function App() {
  return (
    <Routes>
      <Route path="/" element={<MenuPage />} />
      <Route path="/login" element={<StaffManagementPage />} />
      <Route path="/admin/dishes" element={protect(["Administrator"], <AdminDishesPage />)} />
      <Route path="/admin/staff" element={<StaffManagementPage />} />
      <Route path="/admin/settings" element={protect(["Administrator"], <RestaurantSettingsPage />)} />
      <Route path="/chef/availability" element={protect(["Chef"], <ChefAvailabilityPage />)} />
      <Route path="/chef/menu" element={protect(["Chef"], <ChefAvailabilityPage />)} />
      <Route path="/chef/orders" element={protect(["Chef"], <KitchenOrdersPage />)} />
      <Route path="/waiter/orders" element={protect(["Waiter"], <WaiterOrderPage />)} />
      <Route path="/invitations/accept" element={<AcceptInvitationPage />} />
      <Route path="*" element={<MenuPage />} />
    </Routes>
  );
}

export default App;
