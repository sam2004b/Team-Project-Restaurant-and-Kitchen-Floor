import { useCallback, useEffect, useState } from "react";
import type { FormEvent } from "react";
import { Link } from "react-router-dom";
import {
  ApiError,
  createDiningTable,
  createRestaurantSettings,
  getDiningTables,
  getRestaurantSettings,
  getWaiterTables,
  removeTableAssignment,
  saveTableAssignment,
  updateDiningTable,
  updateRestaurantSettings,
} from "./services/api";
import type {
  DiningTableResponse,
  RestaurantSettingsResponse,
  StaffMemberResponse,
  WaiterTableResponse,
} from "./services/api";

type TableDraft = {
  tableNumber: string;
  seatCount: string;
  isActive: boolean;
};

type OperationNotice = {
  type: "success" | "warning" | "error";
  message: string;
};

type ConfigurationTab = "settings" | "tables" | "assignments";

function isActiveWaiter(member: StaffMemberResponse): boolean {
  const active = member.status === 1
    || String(member.status).toLowerCase() === "active";
  return active && member.role === "Waiter";
}

function getErrorMessage(error: unknown, fallback: string): string {
  return error instanceof Error ? error.message : fallback;
}

function validatePositiveInteger(value: string, label: string): string | null {
  const number = Number(value);
  return Number.isSafeInteger(number) && number > 0
    ? null
    : `${label} must be a positive whole number.`;
}

export function RestaurantConfigurationPanel({
  restaurantId,
  members,
}: {
  restaurantId: number;
  members: StaffMemberResponse[];
}) {
  const [settings, setSettings] = useState<RestaurantSettingsResponse | null>(null);
  const [seatCapacity, setSeatCapacity] = useState("4");
  const [settingsLoading, setSettingsLoading] = useState(true);
  const [settingsSaving, setSettingsSaving] = useState(false);
  const [settingsError, setSettingsError] = useState<string | null>(null);
  const [settingsNotice, setSettingsNotice] = useState<OperationNotice | null>(null);
  const [tables, setTables] = useState<DiningTableResponse[]>([]);
  const [tableDrafts, setTableDrafts] = useState<Record<number, TableDraft>>({});
  const [assignmentDrafts, setAssignmentDrafts] = useState<Record<number, string>>({});
  const [tablesLoading, setTablesLoading] = useState(true);
  const [tableError, setTableError] = useState<string | null>(null);
  const [tableNotice, setTableNotice] = useState<OperationNotice | null>(null);
  const [creatingTable, setCreatingTable] = useState(false);
  const [savingTableId, setSavingTableId] = useState<number | null>(null);
  const [tableNumberDraft, setTableNumberDraft] = useState("");
  const [seatCountDraft, setSeatCountDraft] = useState("");
  const [createTableErrors, setCreateTableErrors] = useState<string[]>([]);
  const [activeTab, setActiveTab] = useState<ConfigurationTab>("settings");
  const [tableSearch, setTableSearch] = useState("");
  const [tableStatus, setTableStatus] = useState("all");
  const [editingTableId, setEditingTableId] = useState<number | null>(null);
  const [createTableDialogOpen, setCreateTableDialogOpen] = useState(false);
  const waiters = members.filter(isActiveWaiter);
  const defaultTableSeatCount = String(settings?.defaultSeatCapacity ?? 4);
  const configuredSeats = tables.reduce((total, table) => total + table.seatCount, 0);
  const assignedTables = tables.filter((table) => table.assignment !== null).length;
  const filteredTables = tables.filter((table) => {
    const matchesSearch = String(table.tableNumber).includes(tableSearch.trim());
    const matchesStatus = tableStatus === "all"
      || (tableStatus === "active" && table.isActive)
      || (tableStatus === "inactive" && !table.isActive)
      || (tableStatus === "assigned" && table.assignment !== null)
      || (tableStatus === "unassigned" && table.assignment === null);
    return matchesSearch && matchesStatus;
  });

  const refreshSettings = useCallback(async (): Promise<RestaurantSettingsResponse | null | false> => {
    setSettingsLoading(true);
    setSettingsError(null);
    try {
      const currentSettings = await getRestaurantSettings(restaurantId);
      setSettings(currentSettings);
      setSeatCapacity(String(currentSettings.defaultSeatCapacity));
      return currentSettings;
    } catch (error) {
      if (error instanceof ApiError && error.status === 404) {
        setSettings(null);
        setSeatCapacity("4");
        return null;
      }
      setSettingsError(getErrorMessage(error, "Could not load restaurant settings."));
      return false;
    } finally {
      setSettingsLoading(false);
    }
  }, [restaurantId]);

  const refreshTables = useCallback(async (): Promise<boolean> => {
    setTablesLoading(true);
    setTableError(null);
    try {
      const currentTables = await getDiningTables(restaurantId);
      setTables(currentTables);
      setTableDrafts(Object.fromEntries(currentTables.map((table) => [
        table.id,
        {
          tableNumber: String(table.tableNumber),
          seatCount: String(table.seatCount),
          isActive: table.isActive,
        },
      ])));
      setAssignmentDrafts(Object.fromEntries(currentTables.map((table) => [
        table.id,
        table.assignment ? String(table.assignment.staffMembershipId) : "",
      ])));
      return true;
    } catch (error) {
      setTableError(getErrorMessage(error, "Could not load restaurant tables."));
      return false;
    } finally {
      setTablesLoading(false);
    }
  }, [restaurantId]);

  useEffect(() => {
    void refreshSettings();
    void refreshTables();
  }, [refreshSettings, refreshTables]);

  const saveSettings = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const validationError = validatePositiveInteger(seatCapacity, "Default seat capacity");
    if (validationError) {
      setSettingsError(validationError);
      setSettingsNotice(null);
      return;
    }

    setSettingsSaving(true);
    setSettingsError(null);
    setSettingsNotice(null);
    try {
      const saved = settings
        ? await updateRestaurantSettings(restaurantId, Number(seatCapacity))
        : await createRestaurantSettings(restaurantId, Number(seatCapacity));
      setSettings(saved);
      setSeatCapacity(String(saved.defaultSeatCapacity));
      const reloadedSettings = await refreshSettings();
      if (reloadedSettings) {
        setSettingsNotice({ type: "success", message: "Settings saved and reloaded from the server." });
      } else {
        setSettingsNotice({
          type: "warning",
          message: reloadedSettings === null
            ? "Settings were saved, but the server did not return them when reloaded."
            : "Settings were saved by the server, but the latest values could not be reloaded.",
        });
      }
    } catch (error) {
      setSettingsError(`Settings were not saved: ${getErrorMessage(error, "request failed.")}`);
    } finally {
      setSettingsSaving(false);
    }
  };

  const addTable = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const requestedSeatCount = seatCountDraft || defaultTableSeatCount;
    const validationErrors = [
      validatePositiveInteger(tableNumberDraft, "Table number"),
      validatePositiveInteger(requestedSeatCount, "Seat count"),
    ].filter((message): message is string => message !== null);
    setCreateTableErrors(validationErrors);
    setTableNotice(null);
    if (validationErrors.length > 0) return;

    setCreatingTable(true);
    try {
      await createDiningTable(
        restaurantId,
        Number(tableNumberDraft),
        Number(requestedSeatCount),
        true,
      );
      if (await refreshTables()) {
        setTableNumberDraft("");
        setSeatCountDraft("");
        setCreateTableErrors([]);
        setCreateTableDialogOpen(false);
        setTableNotice({ type: "success", message: "Table created and the table list was reloaded from the server." });
      } else {
        setTableNotice({ type: "warning", message: "Table was created, but the updated table list could not be reloaded." });
      }
    } catch (error) {
      const message = getErrorMessage(error, "request failed.");
      setCreateTableErrors([message]);
      setTableNotice({ type: "error", message: `Table was not created: ${message}` });
    } finally {
      setCreatingTable(false);
    }
  };

  const saveTable = async (table: DiningTableResponse) => {
    const draft = tableDrafts[table.id];
    if (!draft) return;
    const validationErrors = [
      validatePositiveInteger(draft.tableNumber, "Table number"),
      validatePositiveInteger(draft.seatCount, "Seat count"),
    ].filter((message): message is string => message !== null);
    setTableNotice(null);
    if (validationErrors.length > 0) {
      setTableNotice({ type: "error", message: validationErrors.join(" ") });
      return;
    }

    setSavingTableId(table.id);
    try {
      await updateDiningTable(
        restaurantId,
        table.id,
        Number(draft.tableNumber),
        Number(draft.seatCount),
        draft.isActive,
      );
      const refreshed = await refreshTables();
      setTableNotice(refreshed
        ? { type: "success", message: `Table ${draft.tableNumber} saved and reloaded from the server.` }
        : { type: "warning", message: `Table ${draft.tableNumber} was saved, but the latest table list could not be reloaded.` });
      if (refreshed) setEditingTableId(null);
    } catch (error) {
      setTableNotice({ type: "error", message: `Table was not updated: ${getErrorMessage(error, "request failed.")}` });
    } finally {
      setSavingTableId(null);
    }
  };

  const saveAssignment = async (table: DiningTableResponse) => {
    const membershipId = Number(assignmentDrafts[table.id]);
    setSavingTableId(table.id);
    setTableNotice(null);
    try {
      if (membershipId > 0) {
        await saveTableAssignment(restaurantId, table.id, membershipId);
      } else if (table.assignment) {
        await removeTableAssignment(restaurantId, table.id);
      } else {
        return;
      }
      const refreshed = await refreshTables();
      setTableNotice(refreshed
        ? { type: "success", message: `Table ${table.tableNumber} assignment saved and reloaded from the server.` }
        : { type: "warning", message: `Table ${table.tableNumber} assignment was saved, but the latest table list could not be reloaded.` });
    } catch (error) {
      setTableNotice({ type: "error", message: `Assignment was not changed: ${getErrorMessage(error, "request failed.")}` });
    } finally {
      setSavingTableId(null);
    }
  };

  return (
    <div id="restaurant-configuration" className="restaurant-configuration">
      <header className="restaurant-configuration-header">
        <div>
          <p className="eyebrow">WORKSPACE / RESTAURANT MANAGEMENT</p>
          <h2>Restaurant settings</h2>
          <p className="subtitle">Manage seat capacity, dining tables, and waiter assignments.</p>
        </div>
        <span className="restaurant-configuration-id">Restaurant #{restaurantId}</span>
      </header>

      <nav className="configuration-tabs" aria-label="Restaurant configuration">
        <button type="button" className={activeTab === "settings" ? "active" : ""} aria-current={activeTab === "settings" ? "page" : undefined} onClick={() => setActiveTab("settings")}>General settings</button>
        <button type="button" className={activeTab === "tables" ? "active" : ""} aria-current={activeTab === "tables" ? "page" : undefined} onClick={() => setActiveTab("tables")}>Tables</button>
        <button type="button" className={activeTab === "assignments" ? "active" : ""} aria-current={activeTab === "assignments" ? "page" : undefined} onClick={() => setActiveTab("assignments")}>Staff assignment</button>
      </nav>

      <section className="configuration-summary" aria-label="Dining summary">
        <article><span>Configured tables</span><strong>{tables.length}</strong></article>
        <article><span>Total configured seats</span><strong>{configuredSeats}</strong></article>
        <article><span>Staff assignments</span><strong>{assignedTables}</strong></article>
      </section>

      {activeTab === "settings" ? (
      <section className="staff-card" aria-labelledby="restaurant-settings-title">
        <div className="staff-card-heading">
          <div>
            <p className="eyebrow">RESTAURANT SETTINGS</p>
            <h2 id="restaurant-settings-title">Default table capacity</h2>
          </div>
          <button type="button" className="secondary-button" disabled={settingsLoading || settingsSaving} onClick={() => void refreshSettings()}>
            {settingsLoading ? "Loading..." : "Refresh"}
          </button>
        </div>
        <p className="staff-help">Restaurant #{restaurantId}. Settings are loaded from and saved to the backend.</p>
        {settingsError && <p className="staff-notice error" role="alert">{settingsError}</p>}
        {settingsNotice && <p className={`staff-notice ${settingsNotice.type}`} role={settingsNotice.type === "error" ? "alert" : "status"}>{settingsNotice.message}</p>}
        <form className="staff-profile-form" onSubmit={(event) => void saveSettings(event)}>
          <label>
            Default seats per table
            <input
              type="number"
              min="1"
              step="1"
              required
              value={seatCapacity}
              onChange={(event) => setSeatCapacity(event.target.value)}
            />
          </label>
          <button type="submit" className="primary-button" disabled={settingsSaving || settingsLoading}>
            {settingsSaving ? "Saving..." : settings ? "Save settings" : "Create settings"}
          </button>
        </form>
      </section>
      ) : (
      <section className="staff-card" aria-labelledby="dining-tables-title">
        <div className="staff-card-heading">
          <div>
            <p className="eyebrow">{activeTab === "tables" ? "DINING FLOOR" : "ASSIGNMENTS"}</p>
            <h2 id="dining-tables-title">{activeTab === "tables" ? "Tables & seating" : "Staff assignment"}</h2>
          </div>
          <button type="button" className="secondary-button" disabled={tablesLoading || savingTableId !== null} onClick={() => void refreshTables()}>
            {tablesLoading ? "Loading..." : "Refresh"}
          </button>
        </div>
        <p className="staff-help">{activeTab === "tables" ? "Manage table numbers, seat counts, and visibility on the dining floor." : "Choose an active waiter responsible for each active table."}</p>
        {tableError && <p className="staff-notice error" role="alert">{tableError}</p>}
        {tableNotice && <p className={`staff-notice ${tableNotice.type}`} role={tableNotice.type === "error" ? "alert" : "status"}>{tableNotice.message}</p>}

        {activeTab === "tables" && (
          <div className="configuration-table-toolbar">
            <p className="staff-help">Table numbers must be unique. New tables use the configured default seat capacity.</p>
            <button type="button" className="primary-button" onClick={() => {
              setTableNumberDraft("");
              setSeatCountDraft(defaultTableSeatCount);
              setCreateTableErrors([]);
              setCreateTableDialogOpen(true);
            }}>+ New table</button>
          </div>
        )}

        {tablesLoading ? (
          <p className="staff-empty" role="status">Loading tables...</p>
        ) : tableError ? null : tables.length === 0 ? (
          <p className="staff-empty" role="status">No tables are configured for this restaurant.</p>
        ) : (
          <>
          <div className="configuration-table-filters">
            <label>
              <span className="sr-only">Search by table number</span>
              <input type="search" value={tableSearch} onChange={(event) => setTableSearch(event.target.value)} placeholder="Search by table number" />
            </label>
            <label>
              <span className="sr-only">Filter tables</span>
              <select value={tableStatus} onChange={(event) => setTableStatus(event.target.value)}>
                <option value="all">All tables</option>
                <option value="active">Active</option>
                <option value="inactive">Inactive</option>
                <option value="assigned">Assigned</option>
                <option value="unassigned">Unassigned</option>
              </select>
            </label>
          </div>
          {filteredTables.length === 0 ? (
            <p className="staff-empty" role="status">No tables match these filters.</p>
          ) : (
          <div className="staff-table-wrap">
            <table className="staff-table">
              <thead>
                <tr>
                  <th scope="col">Table</th>
                  <th scope="col">Seats</th>
                  <th scope="col">Status</th>
                  <th scope="col">Assigned staff</th>
                  {activeTab === "tables" && <th scope="col">Actions</th>}
                </tr>
              </thead>
              <tbody>
                {filteredTables.map((table) => {
                  const draft = tableDrafts[table.id];
                  if (!draft) return null;
                  const saving = savingTableId === table.id;
                  return (
                    <tr key={table.id}>
                      <td>
                        <strong>Table {table.tableNumber}</strong>
                      </td>
                      <td>{table.seatCount}</td>
                      <td><span className={`staff-status ${table.isActive ? "active" : "revoked"}`}>{table.isActive ? "Active" : "Inactive"}</span></td>
                      <td>
                        {activeTab === "assignments" ? (
                          <div className="configuration-assignment-control">
                            <select
                              aria-label={`Waiter assigned to table ${table.tableNumber}`}
                              value={assignmentDrafts[table.id] ?? ""}
                              disabled={!table.isActive || saving}
                              onChange={(event) => setAssignmentDrafts((current) => ({ ...current, [table.id]: event.target.value }))}
                            >
                              <option value="">Unassigned</option>
                              {table.assignment && !waiters.some((waiter) => waiter.id === table.assignment?.staffMembershipId) && (
                                <option value={String(table.assignment.staffMembershipId)}>
                                  {table.assignment.userName ?? "Inactive waiter"} (inactive)
                                </option>
                              )}
                              {waiters.map((waiter) => (
                                <option key={waiter.id} value={String(waiter.id)}>{waiter.userName}</option>
                              ))}
                            </select>
                            <button type="button" className="staff-text-button" disabled={saving || tablesLoading || (!assignmentDrafts[table.id] && !table.assignment)} onClick={() => void saveAssignment(table)}>
                              {saving ? "Saving..." : "Save assignment"}
                            </button>
                          </div>
                        ) : (
                          table.assignment?.userName ?? "Unassigned"
                        )}
                      </td>
                      {activeTab === "tables" && (
                        <td>
                          <button type="button" className="admin-edit-button" onClick={() => {
                            setTableDrafts((current) => ({ ...current, [table.id]: { tableNumber: String(table.tableNumber), seatCount: String(table.seatCount), isActive: table.isActive } }));
                            setEditingTableId(table.id);
                          }}>Edit</button>
                        </td>
                      )}
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
          )}
          </>
        )}
        {createTableDialogOpen && activeTab === "tables" && (
          <div className="configuration-dialog-backdrop">
            <section className="configuration-dialog" role="dialog" aria-modal="true" aria-labelledby="create-table-title">
              <header>
                <div>
                  <h3 id="create-table-title">Create table</h3>
                  <p>Add a table to the restaurant floor.</p>
                </div>
                <button type="button" aria-label="Close dialog" disabled={creatingTable} onClick={() => setCreateTableDialogOpen(false)}>×</button>
              </header>
              <form className="configuration-dialog-form" onSubmit={(event) => void addTable(event)}>
                <label>Table number<input type="number" min="1" step="1" required value={tableNumberDraft} onChange={(event) => setTableNumberDraft(event.target.value)} /></label>
                <label>
                  Number of seats
                  <span className="configuration-seat-input">
                    <button type="button" aria-label="Decrease seat count" onClick={() => setSeatCountDraft(String(Math.max(1, Number(seatCountDraft || defaultTableSeatCount) - 1)))}>-</button>
                    <input type="number" min="1" step="1" required value={seatCountDraft || defaultTableSeatCount} onChange={(event) => setSeatCountDraft(event.target.value)} />
                    <button type="button" aria-label="Increase seat count" onClick={() => setSeatCountDraft(String(Number(seatCountDraft || defaultTableSeatCount) + 1))}>+</button>
                  </span>
                  <small>Enter a whole number greater than zero.</small>
                </label>
                {createTableErrors.length > 0 && (
                  <ul className="admin-field-error" role="alert">
                    {createTableErrors.map((message, index) => <li key={`${index}-${message}`}>{message}</li>)}
                  </ul>
                )}
                <div className="configuration-dialog-actions">
                  <button type="button" className="secondary-button" disabled={creatingTable} onClick={() => setCreateTableDialogOpen(false)}>Cancel</button>
                  <button type="submit" className="primary-button" disabled={creatingTable}>{creatingTable ? "Saving..." : "Save table"}</button>
                </div>
              </form>
            </section>
          </div>
        )}
        {editingTableId !== null && activeTab === "tables" && (() => {
          const table = tables.find((item) => item.id === editingTableId);
          const draft = tableDrafts[editingTableId];
          if (!table || !draft) return null;
          const saving = savingTableId === table.id;
          return (
            <div className="configuration-dialog-backdrop">
              <section className="configuration-dialog" role="dialog" aria-modal="true" aria-labelledby="edit-table-title">
                <header>
                  <div>
                    <h3 id="edit-table-title">Edit table</h3>
                    <p>Update the table details and save changes.</p>
                  </div>
                  <button type="button" aria-label="Close dialog" disabled={saving} onClick={() => setEditingTableId(null)}>×</button>
                </header>
                <form className="configuration-dialog-form" onSubmit={(event) => {
                  event.preventDefault();
                  void saveTable(table);
                }}>
                  <label>Table number<input type="number" min="1" step="1" required value={draft.tableNumber} onChange={(event) => setTableDrafts((current) => ({ ...current, [table.id]: { ...draft, tableNumber: event.target.value } }))} /></label>
                  <label>
                    Number of seats
                    <span className="configuration-seat-input">
                      <button type="button" aria-label="Decrease seat count" onClick={() => setTableDrafts((current) => ({ ...current, [table.id]: { ...draft, seatCount: String(Math.max(1, Number(draft.seatCount) - 1)) } }))}>-</button>
                      <input type="number" min="1" step="1" required value={draft.seatCount} onChange={(event) => setTableDrafts((current) => ({ ...current, [table.id]: { ...draft, seatCount: event.target.value } }))} />
                      <button type="button" aria-label="Increase seat count" onClick={() => setTableDrafts((current) => ({ ...current, [table.id]: { ...draft, seatCount: String(Number(draft.seatCount) + 1) } }))}>+</button>
                    </span>
                    <small>Enter a whole number greater than zero.</small>
                  </label>
                  <label className="configuration-visibility">
                    <input type="checkbox" checked={draft.isActive} onChange={(event) => setTableDrafts((current) => ({ ...current, [table.id]: { ...draft, isActive: event.target.checked } }))} />
                    <span><strong>Table visibility</strong><small>Active tables appear in the waiter table view.</small></span>
                  </label>
                  {tableNotice?.type === "error" && <p className="staff-notice error" role="alert">{tableNotice.message}</p>}
                  <div className="configuration-dialog-actions">
                    <button type="button" className="secondary-button" disabled={saving} onClick={() => {
                      setTableDrafts((current) => ({ ...current, [table.id]: { tableNumber: String(table.tableNumber), seatCount: String(table.seatCount), isActive: table.isActive } }));
                      setEditingTableId(null);
                    }}>Cancel</button>
                    <button type="submit" className="primary-button" disabled={saving}>{saving ? "Saving..." : "Save table"}</button>
                  </div>
                </form>
              </section>
            </div>
          );
        })()}
      </section>
      )}
    </div>
  );
}

export function WaiterTablesPage() {
  const [tables, setTables] = useState<WaiterTableResponse[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const refresh = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      setTables(await getWaiterTables());
    } catch (caughtError) {
      setError(getErrorMessage(caughtError, "Could not load your assigned tables."));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  return (
    <div className="app-shell admin-shell">
      <aside className="admin-sidebar">
        <Link className="admin-brand" to="/" aria-label="Restaurant customer menu">
          <strong>PLATE</strong>
          <span>RESTAURANT OPERATIONS</span>
        </Link>
        <div className="admin-sidebar-label">WAITER WORKSPACE</div>
        <Link className="admin-sidebar-link" to="/">Customer menu</Link>
        <span className="admin-sidebar-link active" aria-current="page">My tables</span>
      </aside>
      <main className="page admin-page staff-page">
        <header className="admin-header">
          <div>
            <p className="eyebrow">WAITER / TABLES</p>
            <h1>My assigned tables</h1>
            <p className="subtitle">Current table assignments and seat counts from the restaurant configuration.</p>
          </div>
          <button type="button" className="secondary-button" disabled={loading} onClick={() => void refresh()}>
            {loading ? "Refreshing..." : "Refresh"}
          </button>
        </header>
        <section className="staff-card" aria-labelledby="waiter-tables-title">
          <div className="staff-card-heading">
            <div>
              <p className="eyebrow">ASSIGNMENTS</p>
              <h2 id="waiter-tables-title">Tables assigned to you</h2>
            </div>
            <span>{tables.length} tables</span>
          </div>
          {error && <p className="staff-notice error" role="alert">{error}</p>}
          {loading ? (
            <p className="staff-empty" role="status">Loading assigned tables...</p>
          ) : error ? null : tables.length === 0 ? (
            <p className="staff-empty" role="status">No active tables are currently assigned to this waiter account.</p>
          ) : (
            <div className="staff-table-wrap">
              <table className="staff-table">
                <thead><tr><th scope="col">Table</th><th scope="col">Seats</th></tr></thead>
                <tbody>
                  {tables.map((table) => (
                    <tr key={table.id}>
                      <td><strong>Table {table.tableNumber}</strong></td>
                      <td>{table.seatCount}</td>
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
