import { useEffect, useState } from "react";
import type { FormEvent } from "react";
import { Link } from "react-router-dom";
import {
  createDish,
  getAdminDishes,
  getDishDetails,
  updateDish,
} from "./services/api";
import type { DishDetailsResponse, SaveDishRequest } from "./services/api";

type DishFormValues = {
  name: string;
  price: string;
  description: string;
  category: string;
  isAvailable: boolean;
  ingredients: string;
};

const emptyForm: DishFormValues = {
  name: "",
  price: "",
  description: "",
  category: "",
  isAvailable: true,
  ingredients: "",
};

function AdminDishesPage() {
  const [dishes, setDishes] = useState<DishDetailsResponse[]>([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [formError, setFormError] = useState<string | null>(null);
  const [editingId, setEditingId] = useState<number | null>(null);
  const [form, setForm] = useState<DishFormValues>(emptyForm);
  const [formOpen, setFormOpen] = useState(false);

  const loadDishes = async () => {
    setLoading(true);
    setError(null);
    try {
      setDishes(await getAdminDishes());
    } catch (caughtError) {
      setError(
        caughtError instanceof Error
          ? caughtError.message
          : "An unexpected error occurred while loading dishes.",
      );
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void loadDishes();
  }, []);

  const startCreate = () => {
    setEditingId(null);
    setForm(emptyForm);
    setFormError(null);
    setFormOpen(true);
  };

  const startEdit = async (id: number) => {
    setError(null);
    setFormError(null);
    try {
      const dish = await getDishDetails(id);
      setEditingId(dish.id);
      setForm({
        name: dish.name,
        price: String(dish.price),
        description: dish.description ?? "",
        category: dish.category,
        isAvailable: dish.isAvailable,
        ingredients: dish.ingredients.join(", "),
      });
      setFormOpen(true);
    } catch (caughtError) {
      setError(
        caughtError instanceof Error
          ? caughtError.message
          : "An unexpected error occurred while loading the dish.",
      );
    }
  };

  const closeForm = () => {
    setFormOpen(false);
    setEditingId(null);
    setForm(emptyForm);
    setFormError(null);
  };

  const saveDish = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setSaving(true);
    setFormError(null);

    const request: SaveDishRequest = {
      name: form.name.trim(),
      price: Number(form.price),
      description: form.description.trim(),
      category: form.category.trim(),
      isAvailable: form.isAvailable,
      ingredients: form.ingredients
        .split(",")
        .map((ingredient) => ingredient.trim())
        .filter(Boolean),
    };

    try {
      if (editingId === null) {
        await createDish(request);
      } else {
        await updateDish(editingId, request);
      }
      closeForm();
      await loadDishes();
    } catch (caughtError) {
      setFormError(
        caughtError instanceof Error
          ? caughtError.message
          : "An unexpected error occurred while saving the dish.",
      );
    } finally {
      setSaving(false);
    }
  };

  const updateForm = <K extends keyof DishFormValues>(
    key: K,
    value: DishFormValues[K],
  ) => setForm((current) => ({ ...current, [key]: value }));

  return (
    <div className="app-shell admin-shell">
      <main className="page admin-page">
        <header className="admin-header">
          <div>
            <p className="eyebrow">PLATE RESTAURANT</p>
            <h1>Manage dishes</h1>
            <p className="subtitle">Add and update your restaurant menu.</p>
          </div>
          <Link className="admin-back-link" to="/">Customer menu</Link>
        </header>

        <section className="admin-list-section" aria-labelledby="admin-list-title">
          <div className="admin-list-heading">
            <div>
              <h2 id="admin-list-title">Dishes</h2>
              {!loading && !error && <span>{dishes.length} total</span>}
            </div>
            <button type="button" className="primary-button" onClick={startCreate}>
              Add dish
            </button>
          </div>

          {error && (
            <div className="admin-error" role="alert">
              <span>{error}</span>
              <button type="button" onClick={() => void loadDishes()}>Try again</button>
            </div>
          )}

          {loading ? (
            <div className="admin-loading" role="status" aria-live="polite">
              <span className="admin-spinner" aria-hidden="true" />
              Loading dishes...
            </div>
          ) : !error && dishes.length === 0 ? (
            <div className="admin-empty" role="status">
              <h3>No dishes yet</h3>
              <p>Add your first dish to start building the menu.</p>
              <button type="button" className="primary-button" onClick={startCreate}>
                Add first dish
              </button>
            </div>
          ) : !error ? (
            <div className="admin-table-wrap">
              <table className="admin-dish-table">
                <thead>
                  <tr>
                    <th scope="col">Dish</th>
                    <th scope="col">Price</th>
                    <th scope="col">Availability</th>
                    <th scope="col"><span className="sr-only">Actions</span></th>
                  </tr>
                </thead>
                <tbody>
                  {dishes.map((dish) => (
                    <tr key={dish.id}>
                      <td>
                        <strong>{dish.name}</strong>
                        <span className="admin-dish-category">{dish.category}</span>
                      </td>
                      <td>{dish.price.toFixed(2)}</td>
                      <td>
                        <span className={`availability-badge ${dish.isAvailable ? "available" : "unavailable"}`}>
                          {dish.isAvailable ? "Available" : "Unavailable"}
                        </span>
                      </td>
                      <td className="admin-action-cell">
                        <button
                          type="button"
                          className="admin-edit-button"
                          onClick={() => void startEdit(dish.id)}
                        >
                          Edit
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          ) : null}
        </section>

        {formOpen && (
          <div className="admin-modal-backdrop" role="presentation" onMouseDown={(event) => {
            if (event.target === event.currentTarget && !saving) closeForm();
          }}>
            <section
              className="admin-form-panel"
              role="dialog"
              aria-modal="true"
              aria-labelledby="dish-form-title"
            >
              <div className="admin-form-heading">
                <div>
                  <p className="eyebrow">{editingId === null ? "NEW MENU ITEM" : "UPDATE MENU ITEM"}</p>
                  <h2 id="dish-form-title">{editingId === null ? "Add a dish" : "Edit dish"}</h2>
                </div>
                <button
                  type="button"
                  className="admin-close-button"
                  aria-label="Close form"
                  onClick={closeForm}
                  disabled={saving}
                >
                  ×
                </button>
              </div>

              <form className="admin-dish-form" onSubmit={(event) => void saveDish(event)}>
                <label>
                  Dish name
                  <input
                    autoFocus
                    required
                    maxLength={150}
                    value={form.name}
                    onChange={(event) => updateForm("name", event.target.value)}
                  />
                </label>
                <div className="admin-form-row">
                  <label>
                    Price
                    <input
                      type="number"
                      required
                      min="0.01"
                      max="99999999.99"
                      step="0.01"
                      value={form.price}
                      onChange={(event) => updateForm("price", event.target.value)}
                    />
                  </label>
                  <label>
                    Category
                    <input
                      required
                      maxLength={80}
                      value={form.category}
                      onChange={(event) => updateForm("category", event.target.value)}
                    />
                  </label>
                </div>
                <label>
                  Description
                  <textarea
                    maxLength={1000}
                    rows={3}
                    value={form.description}
                    onChange={(event) => updateForm("description", event.target.value)}
                  />
                </label>
                <label>
                  Ingredients <span className="admin-field-hint">(separate with commas)</span>
                  <input
                    value={form.ingredients}
                    onChange={(event) => updateForm("ingredients", event.target.value)}
                  />
                </label>
                <label className="admin-availability-input">
                  <input
                    type="checkbox"
                    checked={form.isAvailable}
                    onChange={(event) => updateForm("isAvailable", event.target.checked)}
                  />
                  Available to order
                </label>

                {formError && <p className="admin-form-error" role="alert">{formError}</p>}

                <div className="admin-form-actions">
                  <button type="button" className="secondary-button" onClick={closeForm} disabled={saving}>
                    Cancel
                  </button>
                  <button type="submit" className="primary-button" disabled={saving}>
                    {saving ? "Saving..." : editingId === null ? "Add dish" : "Save changes"}
                  </button>
                </div>
              </form>
            </section>
          </div>
        )}
      </main>
    </div>
  );
}

export default AdminDishesPage;
