import { useCallback, useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { ApiError, getChefDishes, getCurrentAccount, updateDishAvailability } from "./services/api";
import type { ChefDishResponse } from "./services/api";
import { useMenuUpdates } from "./services/useMenuUpdates";

const stopListStorageKey = "restaurant.chefStopList";

function isChefDish(value: unknown): value is ChefDishResponse {
  if (!value || typeof value !== "object") {
    return false;
  }

  const dish = value as Record<string, unknown>;
  return typeof dish.id === "number"
    && typeof dish.name === "string"
    && typeof dish.category === "string"
    && typeof dish.isAvailable === "boolean";
}

function readStoppedDishes(): ChefDishResponse[] {
  const saved = window.localStorage.getItem(stopListStorageKey);
  if (!saved) {
    return [];
  }

  const parsed: unknown = JSON.parse(saved);
  if (!Array.isArray(parsed) || !parsed.every(isChefDish)) {
    throw new Error("Saved stop-list data is invalid. Clear this browser's saved site data and try again.");
  }

  return parsed;
}

function ChefAvailabilityPage() {
  const [authorized, setAuthorized] = useState<boolean | null>(null);
  const [dishes, setDishes] = useState<ChefDishResponse[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [updating, setUpdating] = useState<Set<number>>(() => new Set());
  const loadVersion = useRef(0);

  useEffect(() => {
    let active = true;
    void getCurrentAccount()
      .then(() => {
        if (active) {
          setAuthorized(true);
        }
      })
      .catch((caughtError: unknown) => {
        if (active) {
          setAuthorized(false);
          setError(caughtError instanceof Error ? caughtError.message : "Could not verify the signed-in account.");
          setLoading(false);
        }
      });

    return () => {
      active = false;
    };
  }, []);

  const refresh = useCallback(async (
    showLoading = true,
    clearPreviousError = true,
  ): Promise<boolean> => {
    const requestVersion = ++loadVersion.current;
    if (showLoading) setLoading(true);
    if (clearPreviousError) setError(null);

    try {
      const currentDishes = await getChefDishes();
      const currentIds = new Set(currentDishes.map((dish) => dish.id));
      const stoppedDishes = readStoppedDishes()
        .filter((dish) => !currentIds.has(dish.id))
        .map((dish) => ({ ...dish, isAvailable: false }));
      window.localStorage.setItem(stopListStorageKey, JSON.stringify(stoppedDishes));
      if (requestVersion === loadVersion.current) {
        setDishes([...currentDishes, ...stoppedDishes]);
      }
      return true;
    } catch (caughtError) {
      if (requestVersion === loadVersion.current) {
        setError(caughtError instanceof Error ? caughtError.message : "Could not load dishes.");
      }
      return false;
    } finally {
      if (requestVersion === loadVersion.current) {
        setLoading(false);
      }
    }
  }, []);

  useEffect(() => {
    if (authorized) {
      void refresh();
    }
  }, [authorized, refresh]);

  useMenuUpdates(() => {
    if (authorized) {
      void refresh(false);
    }
  });

  const changeAvailability = async (dish: ChefDishResponse, isAvailable: boolean) => {
    setError(null);
    setUpdating((current) => new Set(current).add(dish.id));

    try {
      const result = await updateDishAvailability(dish.id, isAvailable);
      if (result.dishId !== dish.id || result.isAvailable !== isAvailable) {
        throw new Error("The server returned an unexpected availability state.");
      }

      setDishes((current) => current.map((item) =>
        item.id === dish.id ? { ...item, isAvailable } : item,
      ));

      let storageError: string | null = null;
      try {
        const stoppedDishes = readStoppedDishes().filter((item) => item.id !== dish.id);
        if (!isAvailable) {
          stoppedDishes.push({ ...dish, isAvailable: false });
        }
        window.localStorage.setItem(stopListStorageKey, JSON.stringify(stoppedDishes));
      } catch (caughtError) {
        storageError = caughtError instanceof Error
          ? caughtError.message
          : "Browser storage is unavailable.";
      }

      const refreshed = await refresh(false, false);
      if (storageError || !refreshed) {
        setError(
          `${dish.name} availability was updated on the server.${
            storageError ? ` Its stopped state could not be saved in this browser: ${storageError}` : ""
          }${!refreshed ? " The list could not be refreshed." : ""}`,
        );
      }
    } catch (caughtError) {
      if (caughtError instanceof ApiError && caughtError.status === 404) {
        setDishes((current) => current.filter((item) => item.id !== dish.id));
        try {
          const stoppedDishes = readStoppedDishes().filter((item) => item.id !== dish.id);
          window.localStorage.setItem(stopListStorageKey, JSON.stringify(stoppedDishes));
        } catch {
          setError("The dish no longer exists on the server, but its stale browser entry could not be removed.");
        }
      }

      const detail = caughtError instanceof ApiError && caughtError.status === 403
        ? "The server refused this update. Verify that this session is signed in as an active Chef for the configured menu restaurant."
        : caughtError instanceof ApiError && caughtError.status === 404
          ? "This dish no longer exists. Its stale row was removed."
        : caughtError instanceof Error
          ? caughtError.message
          : "An unexpected error occurred.";
      const refreshed = await refresh(false, false);
      setError((currentError) =>
        `${currentError ? `${currentError} ` : ""}Could not update ${dish.name} availability. ${detail}${refreshed ? "" : " The dish list could not be refreshed."}`,
      );
    } finally {
      setUpdating((current) => {
        const next = new Set(current);
        next.delete(dish.id);
        return next;
      });
    }
  };

  return (
    <div className="app-shell admin-shell">
      <aside className="admin-sidebar">
        <Link className="admin-brand" to="/" aria-label="Plate restaurant customer menu">
          <strong>PLATE</strong>
          <span>RESTAURANT OPERATIONS</span>
        </Link>
        <div className="admin-sidebar-label">WORKSPACE</div>
        <Link className="admin-sidebar-link" to="/admin/staff">Account and workspaces</Link>
        <Link className="admin-sidebar-link" to="/">Customer menu</Link>
      </aside>

      <main className="page admin-page staff-page">
        <header className="admin-header">
          <div>
            <p className="eyebrow">KITCHEN / CHEF</p>
            <h1>Dish availability</h1>
            <p className="subtitle">Stop-list unavailable dishes and re-enable them when they can be prepared again.</p>
          </div>
          <button type="button" className="secondary-button" disabled={loading || !authorized} onClick={() => void refresh()}>
            {loading ? "Refreshing..." : "Refresh"}
          </button>
        </header>

        <section className="staff-card" aria-labelledby="chef-dishes-title">
          <div className="staff-card-heading">
            <div>
              <p className="eyebrow">KITCHEN</p>
              <h2 id="chef-dishes-title">Menu stop list</h2>
            </div>
            <span className="chef-dish-count">{dishes.length} dishes</span>
          </div>

          <p className="staff-help">
            Available dishes load from the public menu API. Dishes stopped from this browser are saved locally so they can be re-enabled here.
          </p>

          {error && (
            <div className="admin-error" role="alert">
              <span>{error}</span>
              {authorized && (
                <button type="button" onClick={() => void refresh()}>Try again</button>
              )}
            </div>
          )}

          {loading ? (
            <p className="staff-empty" role="status">Loading dish availability...</p>
          ) : !authorized ? (
            <div className="staff-empty">
              <p>Chef access is required to change availability.</p>
              <Link to="/admin/staff">Sign in or switch account</Link>
            </div>
          ) : dishes.length === 0 ? (
            <p className="staff-empty" role="status">No dishes are available in the menu.</p>
          ) : (
            <div className="staff-table-wrap">
              <table className="staff-table">
                <thead>
                  <tr>
                    <th scope="col">Dish</th>
                    <th scope="col">Category</th>
                    <th scope="col">Availability</th>
                    <th scope="col">Stop list</th>
                  </tr>
                </thead>
                <tbody>
                  {dishes.map((dish) => (
                    <tr key={dish.id}>
                      <td><strong>{dish.name}</strong></td>
                      <td>{dish.category}</td>
                      <td>{dish.isAvailable ? "Available" : "Unavailable"}</td>
                      <td>
                        <label className="admin-switch-label staff-availability-switch">
                          <input
                            type="checkbox"
                            checked={!dish.isAvailable}
                            disabled={updating.has(dish.id)}
                            aria-label={`${dish.isAvailable ? "Stop-list" : "Re-enable"} ${dish.name}`}
                            onChange={(event) => void changeAvailability(dish, !event.target.checked)}
                          />
                          <span className="admin-switch" aria-hidden="true" />
                          <span>
                            {updating.has(dish.id)
                              ? "Saving..."
                              : dish.isAvailable
                                ? "Stop-list"
                                : "Re-enable"}
                          </span>
                        </label>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </section>
      </main>
    </div>
  );
}

export default ChefAvailabilityPage;
