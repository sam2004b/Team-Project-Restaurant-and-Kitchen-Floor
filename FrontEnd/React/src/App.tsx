import { useEffect, useMemo, useState } from "react";

type MenuDish = {
  id: number;
  name: string;
  description: string;
  category: string;
  price: number;
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

const apiBaseUrl = (import.meta.env.VITE_API_BASE_URL ?? "").replace(/\/+$/, "");

async function fetchMenu(): Promise<MenuDish[]> {
  const response = await fetch(`${apiBaseUrl}/dishes`);

  if (!response.ok) {
    throw new Error(`Request failed with status ${response.status}`);
  }

  const data = (await response.json()) as Array<{
    id: number;
    name: string;
    description?: string | null;
    category: string;
    price: number;
    isAvailable?: boolean;
  }>;

  return data
    .filter((dish) => dish.isAvailable !== false)
    .map((dish) => ({
      id: dish.id,
      name: dish.name,
      description: dish.description ?? "Chef's special",
      category: dish.category,
      price: Number(dish.price) || 0,
    }));
}

function App() {
  const [dishes, setDishes] = useState<MenuDish[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(false);

  const loadMenu = async () => {
    setLoading(true);
    setError(false);

    try {
      const response = await fetchMenu();
      if (response.length > 0) {
        setDishes(response);
      } else {
        setDishes([]);
      }
    } catch {
      setError(true);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void loadMenu();
  }, []);

  const dishCountText = useMemo(() => `${dishes.length} dishes available`, [dishes.length]);

  return (
    <div className="app-shell">
      <header className="topbar">
        <div className="brand-block">
          <span className="brand">PLATE</span>
          <span className="brand-slash">/</span>
          <span className="brand-sub">MENU</span>
        </div>

        <nav className="main-nav" aria-label="Main menu navigation">
          <span>MENU</span>
          <span>CUSTOMER VIEW</span>
        </nav>

        <button className="help-button" type="button" aria-label="Help">
          ?
        </button>
      </header>

      <main className="page">
        <section className="hero-panel">
          <div className="hero-copy">
            <p className="eyebrow">A LITTLE SOMETHING FOR EVERY TASTE</p>
            <h1>Good food. Great moments.</h1>
            <p className="subtitle">Browse our current menu and discover something delicious.</p>

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
                {loading ? "Updating..." : "Refresh menu"}
              </button>
            </div>
          </div>

          <div className="hero-art" aria-hidden="true">
            <div className="plate-hero food-pasta">
              <span className="plate-core" />
            </div>
          </div>
        </section>

        <section className="menu-header">
          <h2>Available dishes</h2>
          <div className="meta-text">{dishes.length} items</div>
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
            <p>We had trouble retrieving the dishes. Check your connection and try again.</p>
            <button type="button" className="primary-button" onClick={() => void loadMenu()}>
              Try again
            </button>
          </div>
        ) : dishes.length === 0 ? (
          <div className="empty-panel" role="status">
            <div className="empty-badge" aria-hidden="true">~</div>
            <h3>No dishes available</h3>
            <p>There are currently no available dishes. Please check back soon.</p>
            <button type="button" className="primary-button" onClick={() => void loadMenu()}>
              Refresh menu
            </button>
          </div>
        ) : (
          <>
            {error && (
              <div className="refresh-error" role="alert">
                <span>Could not refresh the menu. Your last loaded menu is still shown.</span>
                <button type="button" onClick={() => void loadMenu()}>Try again</button>
              </div>
            )}
            <div className="dish-grid">
              {dishes.map((dish) => (
                <article key={dish.id} className="dish-card">
                  <div className={`dish-art food-${dishArtByName[dish.name] ?? "main"}`} aria-hidden="true">
                    <span className="dish-shadow" />
                    <span className="dish-plate" />
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
          <div className="brand-footer">PLATE</div>
          <div className="footer-note">Menu data and availability are subject to change.</div>
        </footer>
      </main>
    </div>
  );
}

export default App;
