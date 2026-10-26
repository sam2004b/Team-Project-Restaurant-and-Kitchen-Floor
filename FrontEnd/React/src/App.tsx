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

const demoDishes: MenuDish[] = [
  { id: 1, name: "Carbonara", description: "Silky egg & parmesan", category: "Pasta", price: 450 },
  { id: 2, name: "Tomato Soup", description: "Fresh basil & tomatoes", category: "Starters", price: 220 },
  { id: 3, name: "Grilled Chicken", description: "Herb-marinated & tender", category: "Mains", price: 390 },
  { id: 4, name: "House Salad", description: "Leafy greens & dressing", category: "Salads", price: 260 },
  { id: 5, name: "Margherita Pizza", description: "Mozzarella & fresh basil", category: "Pizza", price: 370 },
  { id: 6, name: "Grilled Salmon", description: "Lemon, herbs & greens", category: "Mains", price: 620 },
  { id: 7, name: "Mushroom Risotto", description: "Creamy arborio & mushrooms", category: "Mains", price: 430 },
  { id: 8, name: "Caesar Salad", description: "Crisp romaine & parmesan", category: "Salads", price: 310 },
  { id: 9, name: "Cheesecake", description: "Vanilla & seasonal berry", category: "Desserts", price: 240 },
  { id: 10, name: "Garlic Bread", description: "Golden toasted sourdough", category: "Starters", price: 160 },
  { id: 11, name: "Beef Steak", description: "Chargrilled & rosemary", category: "Mains", price: 720 },
  { id: 12, name: "Lemon Tart", description: "Tangy lemon cream", category: "Desserts", price: 220 },
];

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
  const [dishes, setDishes] = useState<MenuDish[]>(demoDishes);
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
      setDishes(demoDishes);
      setError(false);
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

            <div className="status-pill">
              <span className="status-dot" aria-hidden="true" />
              <span>{dishCountText}</span>
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
        ) : (
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
