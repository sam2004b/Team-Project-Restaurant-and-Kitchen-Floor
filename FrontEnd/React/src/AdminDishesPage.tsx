import { useEffect, useState } from "react";
import type { FormEvent } from "react";
import { Link } from "react-router-dom";
import { apiBaseUrl } from "./services/api";
import {
  ApiError,
  createDish,
  getAdminDishes,
  getDishDetails,
  uploadDishImage,
  updateDish,
} from "./services/api";
import type { DishDetailsResponse, SaveDishRequest } from "./services/api";
import { formatPrice } from "./services/currency";

type DishFormValues = {
  name: string;
  price: string;
  description: string;
  category: string;
  isAvailable: boolean;
  ingredients: string[];
};

type DishFormField = keyof DishFormValues;
type DishFormErrors = Partial<Record<DishFormField, string>>;

function validateDishForm(form: DishFormValues): DishFormErrors {
  const errors: DishFormErrors = {};
  const name = form.name.trim();
  const category = form.category.trim();
  const price = Number(form.price);

  if (!name) {
    errors.name = "Dish name is required.";
  } else if (name.length > 150) {
    errors.name = "Name cannot exceed 150 characters.";
  }

  if (!form.price.trim() || !Number.isFinite(price) || price <= 0 || price > 99999999.99) {
    errors.price = "Enter a price greater than 0 and within the supported range.";
  } else if (Number(price.toFixed(2)) !== price) {
    errors.price = "Price can have at most two decimal places.";
  }

  if (!category) {
    errors.category = "Category is required.";
  } else if (category.length > 80) {
    errors.category = "Category cannot exceed 80 characters.";
  }

  if (form.description.length > 1000) {
    errors.description = "Description cannot exceed 1000 characters.";
  }

  if (form.ingredients.some((ingredient) => !ingredient.trim() || ingredient.trim().length > 150)) {
    errors.ingredients = "Ingredients must be nonblank and no longer than 150 characters.";
  } else if (new Set(form.ingredients.map((ingredient) => ingredient.trim().toLocaleLowerCase())).size !== form.ingredients.length) {
    errors.ingredients = "Ingredients must not contain duplicates.";
  }

  return errors;
}

function mapApiFieldErrors(
  apiErrors: Record<string, string[]>,
): DishFormErrors {
  const mapped: DishFormErrors = {};
  const fields: DishFormField[] = ["name", "price", "category", "description", "ingredients"];

  for (const [apiField, messages] of Object.entries(apiErrors)) {
    const pathSegments = apiField
      .replace(/^\$\.?/, "")
      .split(/[.[\]]/)
      .filter(Boolean);
    const normalizedField = pathSegments[pathSegments.length - 1]?.toLocaleLowerCase();
    const field = fields.find((candidate) => candidate.toLocaleLowerCase() === normalizedField);
    if (field && messages.length > 0) {
      mapped[field] = messages.join(" ");
    }
  }

  return mapped;
}

const emptyForm: DishFormValues = {
  name: "",
  price: "",
  description: "",
  category: "",
  isAvailable: true,
  ingredients: [],
};

const categories = ["Pasta", "Pizza", "Soups", "Salads", "Mains", "Desserts", "Drinks", "Starters"];

function AdminDishesPage() {
  const [dishes, setDishes] = useState<DishDetailsResponse[]>([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [formError, setFormError] = useState<string | null>(null);
  const [fieldErrors, setFieldErrors] = useState<DishFormErrors>({});
  const [editingId, setEditingId] = useState<number | null>(null);
  const [form, setForm] = useState<DishFormValues>(emptyForm);
  const [formOpen, setFormOpen] = useState(false);
  const [ingredientDraft, setIngredientDraft] = useState("");
  const [selectedImage, setSelectedImage] = useState<File | null>(null);
  const [savedDish, setSavedDish] = useState<DishDetailsResponse | null>(null);
  const [saveNotice, setSaveNotice] = useState<{ message: string; isWarning: boolean } | null>(null);

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
    setIngredientDraft("");
    setSelectedImage(null);
    setFormError(null);
    setFieldErrors({});
    setSaveNotice(null);
    setFormOpen(true);
  };

  const startEdit = async (id: number) => {
    setError(null);
    setFormError(null);
    setFieldErrors({});
    try {
      const dish = await getDishDetails(id);
      setEditingId(dish.id);
      setForm({
        name: dish.name,
        price: String(dish.price),
        description: dish.description ?? "",
        category: dish.category,
        isAvailable: dish.isAvailable,
        ingredients: dish.ingredients,
      });
      setIngredientDraft("");
      setSelectedImage(null);
      setSaveNotice(null);
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
    setIngredientDraft("");
    setSelectedImage(null);
    setFormError(null);
  };

  const addIngredient = (value: string) => {
    const ingredient = value.trim().replace(/,$/, "");
    if (ingredient && !form.ingredients.some((item) => item.toLocaleLowerCase() === ingredient.toLocaleLowerCase())) {
      updateForm("ingredients", [...form.ingredients, ingredient]);
    }
    setIngredientDraft("");
  };

  const selectImage = (file: File | undefined) => {
    if (!file) return;
    if (!["image/jpeg", "image/png"].includes(file.type)) {
      setFormError("Choose a JPG or PNG image.");
      return;
    }
    if (file.size > 5 * 1024 * 1024) {
      setFormError("The image must be 5 MB or smaller.");
      return;
    }
    setFormError(null);
    setSelectedImage(file);
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
      ingredients: form.ingredients,
    };

    try {
      let saved: DishDetailsResponse;
      if (editingId === null) {
        saved = await createDish(request);
      } else {
        saved = await updateDish(editingId, request);
      }

      let uploadWarning: string | null = null;
      if (selectedImage) {
        try {
          const uploaded = await uploadDishImage(saved.id, selectedImage);
          saved = { ...saved, imageUrl: uploaded.imageUrl };
        } catch (caughtError) {
          uploadWarning = caughtError instanceof Error
            ? `Dish saved, but the image could not be uploaded: ${caughtError.message}`
            : "Dish saved, but the image could not be uploaded.";
        }
      }

      setSavedDish(saved);
      setSaveNotice({
        message: uploadWarning ?? `${saved.name} ${editingId === null ? "added to" : "updated in"} the restaurant menu.`,
        isWarning: uploadWarning !== null,
      });
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

  const [imagePreview, setImagePreview] = useState<string | null>(null);
  useEffect(() => {
    if (selectedImage) {
      const objectUrl = URL.createObjectURL(selectedImage);
      setImagePreview(objectUrl);
      return () => URL.revokeObjectURL(objectUrl);
    }

    const existingImage = dishes.find((dish) => dish.id === editingId)?.imageUrl;
    setImagePreview(existingImage ? `${apiBaseUrl}${existingImage}` : null);
  }, [dishes, editingId, selectedImage]);

  return (
    <div className="app-shell admin-shell">
      <aside className="admin-sidebar">
        <Link className="admin-brand" to="/" aria-label="Plate restaurant customer menu">
          <strong>PLATE</strong>
          <span>RESTAURANT OPERATIONS</span>
        </Link>
        <div className="admin-sidebar-label">WORKSPACE</div>
        <Link className="admin-sidebar-link" to="/">Overview</Link>
        <span className="admin-sidebar-link active" aria-current="page">Menu management</span>
        <span className="admin-sidebar-link">Availability</span>
        <span className="admin-sidebar-link">Kitchen</span>
        <span className="admin-sidebar-link">Tables</span>
        <div className="admin-profile">
          <span className="admin-avatar" aria-hidden="true">AD</span>
          <span><strong>Restaurant admin</strong><small>Administrator workspace</small></span>
        </div>
      </aside>

      <main className="page admin-page">
        {formOpen ? (
          <>
            <header className="admin-header admin-form-page-header">
              <div>
                <p className="eyebrow">ADMIN / MENU MANAGEMENT / {editingId === null ? "ADD DISH" : "EDIT DISH"}</p>
                <h1>{editingId === null ? "Add new dish" : "Edit dish"}</h1>
                <p className="subtitle">Create an item in your restaurant menu.</p>
              </div>
            </header>
            <button type="button" className="admin-back-link admin-form-back" onClick={closeForm} disabled={saving}>
              ← Back to all dishes
            </button>

            <div className="admin-form-layout">
              <section className="admin-form-card">
                <div className="admin-form-section-heading">
                  <p className="eyebrow">DISH INFORMATION</p>
                  <p>Keep details accurate across the restaurant.</p>
                </div>
                <form className="admin-dish-form admin-dish-form-page" onSubmit={(event) => void saveDish(event)}>
                  <div className="admin-form-row">
                    <label>
                      Dish name <span aria-hidden="true">*</span>
                      <input
                        autoFocus
                        required
                        maxLength={150}
                        value={form.name}
                        aria-invalid={Boolean(fieldErrors.name)}
                        aria-describedby={fieldErrors.name ? "dish-name-error" : undefined}
                        onChange={(event) => updateForm("name", event.target.value)}
                        placeholder="e.g. Carbonara"
                      />
                      {fieldErrors.name && <span id="dish-name-error" className="admin-field-error">{fieldErrors.name}</span>}
                    </label>
                    <label>
                      Price (₽) <span aria-hidden="true">*</span>
                      <input
                        type="number"
                        required
                        min="0.01"
                        max="99999999.99"
                        step="0.01"
                        value={form.price}
                        aria-invalid={Boolean(fieldErrors.price)}
                        aria-describedby={fieldErrors.price ? "dish-price-error" : undefined}
                        onChange={(event) => updateForm("price", event.target.value)}
                        placeholder="0.00"
                      />
                      {fieldErrors.price && <span id="dish-price-error" className="admin-field-error">{fieldErrors.price}</span>}
                    </label>
                  </div>
                  <div className="admin-form-row">
                    <label>
                      Category <span aria-hidden="true">*</span>
                      <select
                        required
                        value={form.category}
                        aria-invalid={Boolean(fieldErrors.category)}
                        aria-describedby={fieldErrors.category ? "dish-category-error" : undefined}
                        onChange={(event) => updateForm("category", event.target.value)}
                      >
                        <option value="" disabled>Select a category</option>
                        {form.category && !categories.includes(form.category) && (
                          <option value={form.category}>{form.category}</option>
                        )}
                        {categories.map((category) => <option key={category} value={category}>{category}</option>)}
                      </select>
                      {fieldErrors.category && <span id="dish-category-error" className="admin-field-error">{fieldErrors.category}</span>}
                    </label>
                    <div className="admin-availability-field">
                      <span>Availability</span>
                      <label className="admin-switch-label">
                        <input
                          type="checkbox"
                          checked={form.isAvailable}
                          onChange={(event) => updateForm("isAvailable", event.target.checked)}
                        />
                        <span className="admin-switch" aria-hidden="true" />
                        <span>Available in customer menu</span>
                      </label>
                    </div>
                  </div>
                  <label>
                    Description
                    <textarea
                      maxLength={1000}
                      rows={3}
                      value={form.description}
                      aria-invalid={Boolean(fieldErrors.description)}
                      aria-describedby={fieldErrors.description ? "dish-description-error" : undefined}
                      onChange={(event) => updateForm("description", event.target.value)}
                      placeholder="Describe the dish for your customers"
                    />
                    {fieldErrors.description && <span id="dish-description-error" className="admin-field-error">{fieldErrors.description}</span>}
                  </label>

                  <div className="admin-image-field">
                    <div className="admin-image-label">
                      <strong>Dish image</strong><span>Optional</span>
                    </div>
                    <div className="admin-image-upload">
                      <div className={`admin-image-preview ${imagePreview ? "has-image" : ""}`}>
                        {imagePreview ? (
                          <img src={imagePreview} alt="Dish preview" />
                        ) : (
                          <>
                            <span className="admin-image-icon" aria-hidden="true">◇</span>
                            <small>No image chosen</small>
                          </>
                        )}
                      </div>
                      <div>
                        <strong>Add a photo of this dish</strong>
                        <p>Images help customers recognise the dish.</p>
                        <label className="secondary-button admin-image-choose">
                          Choose image
                          <input
                            type="file"
                            accept="image/png,image/jpeg"
                            onChange={(event) => selectImage(event.target.files?.[0])}
                          />
                        </label>
                        {selectedImage && (
                          <button type="button" className="admin-remove-image" onClick={() => setSelectedImage(null)}>
                            Remove selected photo
                          </button>
                        )}
                        <small className="admin-image-hint">JPG or PNG · Max 5 MB</small>
                      </div>
                    </div>
                  </div>

                  <div className="admin-ingredients-field">
                    <div className="admin-ingredients-heading">
                      <strong>Ingredients</strong>
                      <span>Add or edit each ingredient</span>
                    </div>
                    <div className="admin-ingredient-chips">
                      {form.ingredients.map((ingredient) => (
                        <span className="admin-ingredient-chip" key={ingredient}>
                          {ingredient}
                          <button
                            type="button"
                            aria-label={`Remove ${ingredient}`}
                            onClick={() => updateForm("ingredients", form.ingredients.filter((item) => item !== ingredient))}
                          >×</button>
                        </span>
                      ))}
                      <input
                        aria-label="Add ingredient"
                        value={ingredientDraft}
                        onChange={(event) => setIngredientDraft(event.target.value)}
                        onKeyDown={(event) => {
                          if (event.key === "Enter" || event.key === ",") {
                            event.preventDefault();
                            addIngredient(ingredientDraft);
                          }
                        }}
                        onBlur={() => {
                          if (ingredientDraft.trim()) addIngredient(ingredientDraft);
                        }}
                        placeholder="Add an ingredient"
                      />
                    </div>
                    <button type="button" className="admin-add-ingredient" onClick={() => addIngredient(ingredientDraft)}>
                      + Add ingredient
                    </button>
                    {fieldErrors.ingredients && <span className="admin-field-error">{fieldErrors.ingredients}</span>}
                  </div>

                  {formError && <p className="admin-form-error" role="alert">{formError}</p>}

                  <div className="admin-form-actions">
                    <button type="button" className="secondary-button" onClick={closeForm} disabled={saving}>
                      Cancel
                    </button>
                    <button type="submit" className="primary-button" disabled={saving}>
                      {saving ? "Saving..." : "Save dish"}
                    </button>
                  </div>
                </form>
              </section>

              <aside className="admin-preview-column">
                <section className="admin-customer-preview">
                  <p className="eyebrow">CUSTOMER VIEW PREVIEW</p>
                  <p className="admin-preview-subtitle">Read-only representation</p>
                  <div className="admin-preview-art">
                    {imagePreview ? <img src={imagePreview} alt="" /> : <span aria-hidden="true">〰</span>}
                  </div>
                  <p className="admin-preview-category">{form.category || "CATEGORY"}</p>
                  <div className="admin-preview-title">
                    <h2>{form.name || "Dish name"}</h2>
                    <strong>{form.price ? formatPrice(Number(form.price)) : "—"}</strong>
                  </div>
                  <span className={`availability-badge ${form.isAvailable ? "available" : "unavailable"}`}>
                    <span className="availability-dot" aria-hidden="true" />
                    {form.isAvailable ? "Available" : "Unavailable"}
                  </span>
                  <p className="admin-preview-description">{form.description || "Your dish description will appear here."}</p>
                  <p className="admin-preview-ingredients-label">INGREDIENTS</p>
                  <p className="admin-preview-ingredients">
                    {form.ingredients.length ? form.ingredients.join(" · ") : "No ingredients added"}
                  </p>
                  <small>Visible to customers after saving</small>
                </section>
                <section className="admin-photo-status">
                  <strong>PHOTO STATUS</strong>
                  <b>{imagePreview ? "Photo ready" : "No photo uploaded"}</b>
                  <p>{imagePreview ? "This photo will appear on your menu." : "Select a photo in the form."}</p>
                  <small>The customer menu uses the saved image.</small>
                </section>
              </aside>
            </div>
          </>
        ) : (
          <>
            <header className="admin-header">
              <div>
                <p className="eyebrow">ADMIN / MENU MANAGEMENT</p>
                <h1>Menu management</h1>
                <p className="subtitle">Add dishes, keep prices current and control availability.</p>
              </div>
              <button type="button" className="primary-button" onClick={startCreate}>+ Add dish</button>
            </header>

            <section className="admin-stats" aria-label="Dish summary">
              <article className="admin-stat-card"><strong>{dishes.length}</strong><span>Total dishes</span></article>
              <article className="admin-stat-card"><strong>{dishes.filter((dish) => dish.isAvailable).length}</strong><span>Available</span></article>
              <article className="admin-stat-card"><strong>{dishes.filter((dish) => !dish.isAvailable).length}</strong><span>Unavailable</span></article>
            </section>

            <section className="admin-list-section" aria-labelledby="admin-list-title">
              {saveNotice && (
                <div className={`admin-save-notice ${saveNotice.isWarning ? "warning" : ""}`} role="status">
                  <span className="admin-save-icon" aria-hidden="true">{saveNotice.isWarning ? "!" : "✓"}</span>
                  <span><strong>{saveNotice.isWarning ? "Dish saved with a photo warning" : "Dish saved successfully"}</strong><small>{saveNotice.message}</small></span>
                  <button type="button" aria-label="Dismiss message" onClick={() => setSaveNotice(null)}>×</button>
                </div>
              )}
              {error && (
                <div className="admin-error" role="alert">
                  <span>{error}</span>
                  <button type="button" onClick={() => void loadDishes()}>Try again</button>
                </div>
              )}
              <div className="admin-list-heading">
                <div><h2 id="admin-list-title">All dishes</h2></div>
              </div>
              {loading ? (
                <div className="admin-loading" role="status" aria-live="polite">
                  <span className="admin-spinner" aria-hidden="true" />Loading dishes...
                </div>
              ) : !error && dishes.length === 0 ? (
                <div className="admin-empty" role="status">
                  <h3>No dishes yet</h3><p>Add your first dish to start building the menu.</p>
                  <button type="button" className="primary-button" onClick={startCreate}>Add first dish</button>
                </div>
              ) : !error ? (
                <>
                  <div className="admin-table-wrap">
                    <table className="admin-dish-table">
                      <thead><tr>
                        <th scope="col">Dish</th><th scope="col">Category</th><th scope="col">Price</th>
                        <th scope="col">Status</th><th scope="col"><span className="sr-only">Actions</span></th>
                      </tr></thead>
                      <tbody>
                        {dishes.map((dish) => (
                          <tr key={dish.id}>
                            <td><div className="admin-dish-name">
                              <span className="admin-dish-thumb">{dish.imageUrl && <img src={`${apiBaseUrl}${dish.imageUrl}`} alt="" />}</span>
                              <span><strong>{dish.name}</strong><span className="admin-dish-category">Dish details &amp; ingredients</span></span>
                            </div></td>
                            <td>{dish.category}</td>
                            <td className="admin-price">{formatPrice(dish.price)}</td>
                            <td><span className={`availability-badge ${dish.isAvailable ? "available" : "unavailable"}`}>
                              <span className="availability-dot" aria-hidden="true" />{dish.isAvailable ? "Available" : "Unavailable"}
                            </span></td>
                            <td className="admin-action-cell"><button type="button" className="admin-edit-button" onClick={() => void startEdit(dish.id)}>Edit</button></td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                  {savedDish && (
                    <div className="admin-saved-details">
                      <div>
                        <strong>Saved data</strong>
                        <p>Name {savedDish.name}</p>
                        <p>Price {formatPrice(savedDish.price)}</p>
                        <p>Ingredients {savedDish.ingredients.join(" · ") || "None listed"}</p>
                      </div>
                      <button type="button" className="secondary-button" onClick={() => void startEdit(savedDish.id)}>
                        Edit {savedDish.name}
                      </button>
                    </div>
                  )}
                </>
              ) : null}
            </section>
          </>
        )}
      </main>
    </div>
  );
}

export default AdminDishesPage;
