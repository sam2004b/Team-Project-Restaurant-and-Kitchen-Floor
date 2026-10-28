import { useEffect, useMemo, useState } from "react";
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

  const loadMenu = async () => {
    setLoading(true);
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
    } catch (caughtError) {
      setError(
        caughtError instanceof Error
          ? caughtError.message
          : "An unexpected error occurred while loading the menu.",
      );
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void loadMenu();
  }, []);

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
                    <button type="button" className="arrow-button" aria-label={`View ${dish.name}`}>
                      <span aria-hidden="true">›</span>
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
