import { useEffect, useMemo, useState } from "react";
import {
  ApiError,
  apiBaseUrl,
  createOrder,
  getMenuDishes,
  getOrder,
  type OrderResponse,
} from "./services/api";
import { Link, Route, Routes } from "react-router-dom";
import AdminDishesPage from "./AdminDishesPage";
import { apiBaseUrl, getMenuDishes } from "./services/api";

type MenuDish = {
  id: number;
  name: string;
  description: string;
  category: string;
  price: number;
  imageUrl: string | null;
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
  const [blockedDishIds, setBlockedDishIds] = useState<Set<number>>(() => new Set());
  const [orderingDishId, setOrderingDishId] = useState<number | null>(null);
  const [orderFeedback, setOrderFeedback] = useState<{
    type: "success" | "error";
    message: string;
  } | null>(null);
  const [lastOrder, setLastOrder] = useState<OrderResponse | null>(null);

  const loadMenu = async (showLoading = true): Promise<boolean> => {
    if (showLoading) {
      setLoading(true);
    }
    setError(null);

    try {
      const response = await getMenuDishes();
      setDishes(
        response
          .filter((dish) => dish.isAvailable)
          .map((dish) => ({
            id: dish.id,
            name: dish.name,
            description: dish.description ?? "Chef's special",
            category: dish.category,
            price: dish.price,
            imageUrl: dish.imageUrl,
          })),
      );
      setBlockedDishIds(new Set());
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

    let savedOrderId: string | null;
    try {
      savedOrderId = window.localStorage.getItem("restaurant.lastOrderId");
    } catch (caughtError) {
      setOrderFeedback({
        type: "error",
        message: `Could not restore your previous order confirmation: ${
          caughtError instanceof Error ? caughtError.message : "browser storage is unavailable."
        }`,
      });
      return;
    }

    if (!savedOrderId) {
      return;
    }

    const orderId = Number(savedOrderId);
    if (!Number.isSafeInteger(orderId) || orderId <= 0) {
      setOrderFeedback({
        type: "error",
        message: "The saved order reference in this browser is invalid.",
      });
      return;
    }

    void getOrder(orderId)
      .then((order) => {
        setLastOrder(order);
        setOrderFeedback({
          type: "success",
          message: `Order #${order.id} is saved on the server.`,
        });
      })
      .catch((caughtError: unknown) => {
        setOrderFeedback({
          type: "error",
          message: `Could not restore order #${orderId}: ${
            caughtError instanceof Error ? caughtError.message : "an unexpected error occurred."
          }`,
        });
      });
  }, []);

  const placeOrder = async (dish: MenuDish) => {
    setOrderingDishId(dish.id);
    setOrderFeedback(null);

    try {
      const order = await createOrder(dish.id);
      setLastOrder(order);
      setOrderFeedback({
        type: "success",
        message: `Order #${order.id} for ${dish.name} was placed successfully.`,
      });

      try {
        window.localStorage.setItem("restaurant.lastOrderId", String(order.id));
      } catch (caughtError) {
        setOrderFeedback({
          type: "success",
          message: `Order #${order.id} for ${dish.name} was placed and saved on the server, but this browser could not save its reference${
            caughtError instanceof Error ? `: ${caughtError.message}` : "."
          }`,
        });
      }
    } catch (caughtError) {
      if (caughtError instanceof ApiError && caughtError.status === 409) {
        setBlockedDishIds((current) => new Set(current).add(dish.id));
        const refreshed = await loadMenu(false);
        setOrderFeedback({
          type: "error",
          message: refreshed
            ? `${dish.name} became unavailable before your order was placed. The menu has been refreshed.`
            : `${dish.name} became unavailable before your order was placed. The menu could not be refreshed; please try again.`,
        });
      } else {
        setOrderFeedback({
          type: "error",
          message: `Could not place your order: ${
            caughtError instanceof Error ? caughtError.message : "an unexpected error occurred."
          }`,
        });
      }
    } finally {
      setOrderingDishId(null);
    }
  };

  const visibleDishes = useMemo(() => {
    if (selectedCategory === "All") {
      return dishes;
    }

    return dishes.filter((dish) => normalizeCategoryName(dish.category) === selectedCategory);
  }, [dishes, selectedCategory]);

  const dishCountText = useMemo(() => `${visibleDishes.length} dishes available`, [visibleDishes.length]);

  return (
    <div className="app-shell">
      <main className="page">
        <section className="hero-panel">
          <div className="hero-copy">
            <p className="eyebrow">WELCOME TO</p>
            <h1>PLATE Restaurant</h1>
            <p className="subtitle">Explore our menu by category</p>

            <div className="hero-status-row">
              <div className="status-pill" aria-live="polite">
                <span className="status-dot" aria-hidden="true" />
                <span>{loading ? "Updating menu..." : dishCountText}</span>
              </div>
              <button
                type="button"
                className="refresh-button"
                onClick={() => void loadMenu()}
                disabled={loading}
              >
                {loading ? "Updating menu..." : "Updated menu"}
              </button>
            </div>
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
          <div className="meta-text">{visibleDishes.length} items</div>
        </section>

        {orderFeedback && (
          <div
            className={`order-feedback ${orderFeedback.type}`}
            role={orderFeedback.type === "error" ? "alert" : "status"}
            aria-live="polite"
          >
            <span>{orderFeedback.message}</span>
            {lastOrder && orderFeedback.type === "success" && (
              <span className="order-summary">
                {lastOrder.items.map((item) => `${item.quantity} × ${item.dishName}`).join(", ")}
              </span>
            )}
          </div>
        )}

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
        ) : visibleDishes.length === 0 ? (
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
            {error && (
              <div className="refresh-error" role="alert">
                <span>Could not refresh the menu. Your last loaded menu is still shown. {error}</span>
                <button type="button" onClick={() => void loadMenu()}>Try again</button>
              </div>
            )}
            <div className="dish-grid">
              {visibleDishes.map((dish) => (
                <article key={dish.id} className="dish-card">
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

                  <div className="dish-meta">
                    <div className="dish-category">{dish.category}</div>
                    <h3>{dish.name}</h3>
                    <p>{dish.description}</p>
                  </div>

                  <div className="dish-footer">
                    <span className="price">{dish.price}</span>
                    <button
                      type="button"
                      className="order-button"
                      aria-label={`Order ${dish.name}`}
                      onClick={() => void placeOrder(dish)}
                      disabled={orderingDishId !== null || blockedDishIds.has(dish.id)}
                    >
                      {orderingDishId === dish.id
                        ? "Ordering..."
                        : blockedDishIds.has(dish.id)
                          ? "Unavailable"
                          : "Order"}
                    </button>
                  </div>
                </article>
              ))}
            </div>
          </>
        )}

        <footer className="footer-bar">
          <Link className="admin-menu-link" to="/admin/dishes">Manage dishes</Link>
        </footer>
      </main>
    </div>
  );
}

function App() {
  return (
    <Routes>
      <Route path="/" element={<MenuPage />} />
      <Route path="/admin/dishes" element={<AdminDishesPage />} />
      <Route path="*" element={<MenuPage />} />
    </Routes>
  );
}

export default App;
