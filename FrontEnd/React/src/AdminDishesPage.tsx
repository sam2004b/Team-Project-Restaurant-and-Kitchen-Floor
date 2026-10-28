import { useEffect, useState } from "react";
import type { FormEvent } from "react";
import { Link } from "react-router-dom";
import {
  ApiError,
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

type DishFormField = keyof DishFormValues;
type DishFormErrors = Partial<Record<DishFormField, string>>;

const dishFormFields: DishFormField[] = [
  "name",
  "price",
  "description",
  "category",
  "isAvailable",
  "ingredients",
];

function validateDishForm(form: DishFormValues): DishFormErrors {
  const errors: DishFormErrors = {};
  const name = form.name.trim();
  const category = form.category.trim();
  const price = form.price.trim();
  const ingredients = form.ingredients
    .split(",")
    .map((ingredient) => ingredient.trim())
    .filter(Boolean);

  if (!name) {
    errors.name = "Dish name is required.";
  } else if (name.length > 150) {
    errors.name = "Name cannot exceed 150 characters.";
  }

  const numericPrice = Number(price);
  if (!price || !Number.isFinite(numericPrice) || numericPrice <= 0) {
    errors.price = "Enter a price greater than 0.";
  } else if (numericPrice > 99999999.99) {
    errors.price = "Price is above the allowed maximum.";
  } else if (!/^(?:\d+|\d*\.\d{1,2})$/.test(price)) {
    errors.price = "Price can have no more than two decimal places.";
  }

  if (!category) {
    errors.category = "Category is required.";
  } else if (category.length > 80) {
    errors.category = "Category cannot exceed 80 characters.";
  }

  if (form.description.trim().length > 1000) {
    errors.description = "Description cannot exceed 1000 characters.";
  }

  if (ingredients.some((ingredient) => ingredient.length > 150)) {
    errors.ingredients = "Ingredient names cannot exceed 150 characters.";
  } else if (new Set(ingredients.map((ingredient) => ingredient.toLowerCase())).size !== ingredients.length) {
    errors.ingredients = "Duplicate ingredients are not allowed.";
  }

  return errors;
}

function mapApiFieldErrors(errors: Record<string, string[]>): DishFormErrors {
  const mapped: DishFormErrors = {};

  for (const [field, messages] of Object.entries(errors)) {
    const fieldName = field.replace(/\[\d+\]/g, "").split(".").pop()?.toLowerCase();
    const matchingField = dishFormFields.find((candidate) => candidate.toLowerCase() === fieldName);
    if (matchingField && messages.length > 0) {
      mapped[matchingField] = messages.join(" ");
    }
  }

  return mapped;
}

function AdminDishesPage() {
  const [dishes, setDishes] = useState<DishDetailsResponse[]>([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [formError, setFormError] = useState<string | null>(null);
  const [fieldErrors, setFieldErrors] = useState<DishFormErrors>({});
  const [successMessage, setSuccessMessage] = useState<string | null>(null);
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
    setFieldErrors({});
    setSuccessMessage(null);
    setFormOpen(true);
  };

  const startEdit = async (id: number) => {
    setError(null);
    setFormError(null);
    setFieldErrors({});
    setSuccessMessage(null);
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
    setFieldErrors({});
  };

  const saveDish = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setFormError(null);
    setFieldErrors({});

    const validationErrors = validateDishForm(form);
    if (Object.keys(validationErrors).length > 0) {
      setFieldErrors(validationErrors);
      return;
    }

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
      setSuccessMessage(
        `${request.name} was ${editingId === null ? "added to" : "updated in"} the menu.`,
      );
      closeForm();
      await loadDishes();
    } catch (caughtError) {
      if (caughtError instanceof ApiError) {
        const apiFieldErrors = mapApiFieldErrors(caughtError.fieldErrors);
        setFieldErrors(apiFieldErrors);
        setFormError(
          Object.keys(apiFieldErrors).length === 0 ? caughtError.message : null,
        );
      } else {
        setFormError(
          caughtError instanceof Error
            ? caughtError.message
            : "An unexpected error occurred while saving the dish.",
        );
      }
    } finally {
      setSaving(false);
    }
  };

  const updateForm = <K extends keyof DishFormValues>(
    key: K,
    value: DishFormValues[K],
  ) => {
    setForm((current) => ({ ...current, [key]: value }));
    setFieldErrors((current) => {
      if (!current[key]) {
        return current;
      }

      const next = { ...current };
      delete next[key];
      return next;
    });
    setFormError(null);
  };

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

          {successMessage && (
            <div className="admin-success" role="status" aria-live="polite">
              {successMessage}
            </div>
          )}

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

              <form
                className="admin-dish-form"
                noValidate
                onSubmit={(event) => void saveDish(event)}
              >
                <label>
                  Dish name
                  <input
                    autoFocus
                    maxLength={150}
                    value={form.name}
                    onChange={(event) => updateForm("name", event.target.value)}
                    aria-invalid={Boolean(fieldErrors.name)}
                    aria-describedby={fieldErrors.name ? "dish-name-error" : undefined}
                  />
                  {fieldErrors.name && <span className="admin-field-error" id="dish-name-error">{fieldErrors.name}</span>}
                </label>
                <div className="admin-form-row">
                  <label>
                    Price
                    <input
                      type="number"
                      min="0.01"
                      max="99999999.99"
                      step="0.01"
                      value={form.price}
                      onChange={(event) => updateForm("price", event.target.value)}
                      aria-invalid={Boolean(fieldErrors.price)}
                      aria-describedby={fieldErrors.price ? "dish-price-error" : undefined}
                    />
                    {fieldErrors.price && <span className="admin-field-error" id="dish-price-error">{fieldErrors.price}</span>}
                  </label>
                  <label>
                    Category
                    <input
                      maxLength={80}
                      value={form.category}
                      onChange={(event) => updateForm("category", event.target.value)}
                      aria-invalid={Boolean(fieldErrors.category)}
                      aria-describedby={fieldErrors.category ? "dish-category-error" : undefined}
                    />
                    {fieldErrors.category && <span className="admin-field-error" id="dish-category-error">{fieldErrors.category}</span>}
                  </label>
                </div>
                <label>
                  Description
                  <textarea
                    maxLength={1000}
                    rows={3}
                    value={form.description}
                    onChange={(event) => updateForm("description", event.target.value)}
                    aria-invalid={Boolean(fieldErrors.description)}
                    aria-describedby={fieldErrors.description ? "dish-description-error" : undefined}
                  />
                  {fieldErrors.description && <span className="admin-field-error" id="dish-description-error">{fieldErrors.description}</span>}
                </label>
                <label>
                  Ingredients <span className="admin-field-hint">(separate with commas)</span>
                  <input
                    value={form.ingredients}
                    onChange={(event) => updateForm("ingredients", event.target.value)}
                    aria-invalid={Boolean(fieldErrors.ingredients)}
                    aria-describedby={fieldErrors.ingredients ? "dish-ingredients-error" : undefined}
                  />
                  {fieldErrors.ingredients && <span className="admin-field-error" id="dish-ingredients-error">{fieldErrors.ingredients}</span>}
                </label>
                <label className="admin-availability-switch">
                  <input
                    type="checkbox"
                    role="switch"
                    checked={form.isAvailable}
                    onChange={(event) => updateForm("isAvailable", event.target.checked)}
                  />
                  <span className="admin-switch-track" aria-hidden="true">
                    <span />
                  </span>
                  <span className="admin-switch-copy">
                    <strong>{form.isAvailable ? "Available to order" : "Stop-listed"}</strong>
                    <small>
                      {form.isAvailable
                        ? "Customers can see and order this dish."
                        : "This dish is hidden from the customer menu."}
                    </small>
                  </span>
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
