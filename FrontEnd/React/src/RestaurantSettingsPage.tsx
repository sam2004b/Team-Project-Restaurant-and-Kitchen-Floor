import { useCallback, useEffect, useMemo, useState } from "react";
import type { FormEvent } from "react";
import { Link } from "react-router-dom";
import {
  ApiError,
  clearStoredAccessToken,
  getCurrentAccount,
  getDiningTables,
  getRestaurantProfile,
  getRestaurantSettings,
  getRestaurantStaff,
  getStoredAccessToken,
  login,
  removeTableAssignment,
  saveDiningTable,
  saveRestaurantSettings,
  saveTableAssignment,
  storeAccessToken,
  updateRestaurantProfile,
} from "./services/api";
import type {
  AccountResponse,
  DiningTableResponse,
  RestaurantResponse,
  RestaurantSettingsResponse,
  StaffMemberResponse,
} from "./services/api";

type Notice = { type: "success" | "error" | "warning"; message: string };
type SettingsData = {
  profile: RestaurantResponse;
  settings: RestaurantSettingsResponse | null;
  tables: DiningTableResponse[];
  staff: StaffMemberResponse[];
};
type ProfileErrors = Partial<Record<"name" | "address", string>>;
type TableFormValues = {
  tableNumber: string;
  seatCount: string;
  isActive: boolean;
  staffMembershipId: string;
};
type TableErrors = Partial<Record<"tableNumber" | "seatCount" | "staffMembershipId", string>>;

const emptyTableForm: TableFormValues = {
  tableNumber: "",
  seatCount: "4",
  isActive: true,
  staffMembershipId: "",
};

function getErrorMessage(error: unknown, fallback: string): string {
  return error instanceof Error ? error.message : fallback;
}

function isActiveMembership(status: number | string): boolean {
  const normalized = String(status).toLowerCase();
  return normalized === "1" || normalized === "active";
}

function isPositiveInteger(value: string): boolean {
  const parsed = Number(value);
  return /^\d+$/.test(value) && Number.isSafeInteger(parsed) && parsed > 0 && parsed <= 2147483647;
}

async function fetchSettingsData(restaurantId: number): Promise<SettingsData> {
  const settingsRequest = getRestaurantSettings(restaurantId).catch((error: unknown) => {
    if (error instanceof ApiError && error.status === 404) {
      return null;
    }
    throw error;
  });
  const [profile, settings, tables, staff] = await Promise.all([
    getRestaurantProfile(restaurantId),
    settingsRequest,
    getDiningTables(restaurantId),
    getRestaurantStaff(restaurantId),
  ]);
  return { profile, settings, tables, staff };
}

function RestaurantSettingsPage() {
  const [account, setAccount] = useState<AccountResponse | null>(null);
  const [sessionChecking, setSessionChecking] = useState(true);
  const [authNotice, setAuthNotice] = useState<Notice | null>(null);
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [authSubmitting, setAuthSubmitting] = useState(false);

  const [settingsData, setSettingsData] = useState<SettingsData | null>(null);
  const [loading, setLoading] = useState(false);
  const [pageError, setPageError] = useState<string | null>(null);
  const [activeTab, setActiveTab] = useState<"profile" | "tables">("profile");

  const [profileName, setProfileName] = useState("");
  const [profileAddress, setProfileAddress] = useState("");
  const [profileErrors, setProfileErrors] = useState<ProfileErrors>({});
  const [profileNotice, setProfileNotice] = useState<Notice | null>(null);
  const [savingProfile, setSavingProfile] = useState(false);

  const [defaultSeatCapacity, setDefaultSeatCapacity] = useState("4");
  const [capacityError, setCapacityError] = useState<string | null>(null);
  const [capacityNotice, setCapacityNotice] = useState<Notice | null>(null);
  const [savingCapacity, setSavingCapacity] = useState(false);

  const [editorOpen, setEditorOpen] = useState(false);
  const [editingTableId, setEditingTableId] = useState<number | null>(null);
  const [tableForm, setTableForm] = useState<TableFormValues>(emptyTableForm);
  const [tableErrors, setTableErrors] = useState<TableErrors>({});
  const [tableNotice, setTableNotice] = useState<Notice | null>(null);
  const [savingTable, setSavingTable] = useState(false);

  const restaurantId = Number(account?.restaurantId);
  const validRestaurantId = Number.isSafeInteger(restaurantId) && restaurantId > 0;

  useEffect(() => {
    let active = true;
    try {
      const token = getStoredAccessToken();
      if (!token) {
        setSessionChecking(false);
        return () => { active = false; };
      }

      void getCurrentAccount()
        .then((currentAccount) => {
          if (active) {
            setAccount(currentAccount);
            setAuthNotice(null);
          }
        })
        .catch((error: unknown) => {
          if (active) {
            setAuthNotice({
              type: "error",
              message: `Could not verify the signed-in account: ${getErrorMessage(error, "the session is invalid or expired.")}`,
            });
          }
        })
        .finally(() => {
          if (active) setSessionChecking(false);
        });
    } catch (error) {
      setAuthNotice({
        type: "error",
        message: `Could not read the signed-in session: ${getErrorMessage(error, "session storage is unavailable.")}`,
      });
      setSessionChecking(false);
    }

    return () => { active = false; };
  }, []);

  const refreshRestaurant = useCallback(async () => {
    if (!validRestaurantId) return;
    setLoading(true);
    setPageError(null);
    try {
      const data = await fetchSettingsData(restaurantId);
      setSettingsData(data);
    } catch (error) {
      setPageError(getErrorMessage(error, "Could not load restaurant settings."));
    } finally {
      setLoading(false);
    }
  }, [restaurantId, validRestaurantId]);

  useEffect(() => {
    if (account && validRestaurantId) {
      void refreshRestaurant();
    }
  }, [account, refreshRestaurant, validRestaurantId]);

  useEffect(() => {
    if (settingsData) {
      setProfileName(settingsData.profile.name);
      setProfileAddress(settingsData.profile.address);
      setDefaultSeatCapacity(String(settingsData.settings?.defaultSeatCapacity ?? 4));
      setProfileErrors({});
      setProfileNotice(null);
      setCapacityError(null);
    }
  }, [settingsData]);

  const activeWaiters = useMemo(
    () => (settingsData?.staff ?? []).filter(
      (member) => member.role.toLowerCase() === "waiter" && isActiveMembership(member.status),
    ),
    [settingsData?.staff],
  );

  const submitLogin = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setAuthSubmitting(true);
    setAuthNotice(null);
    try {
      const response = await login(username.trim(), password);
      if (!response.accessToken) {
        throw new Error("The API did not return an access token.");
      }
      storeAccessToken(response.accessToken);
      const currentAccount = await getCurrentAccount();
      setAccount(currentAccount);
      setPassword("");
      setAuthNotice({ type: "success", message: `Signed in as ${currentAccount.userName}.` });
    } catch (error) {
      setAuthNotice({ type: "error", message: getErrorMessage(error, "Could not sign in.") });
    } finally {
      setAuthSubmitting(false);
      setSessionChecking(false);
    }
  };

  const signOut = () => {
    try {
      clearStoredAccessToken();
      setAccount(null);
      setSettingsData(null);
      setAuthNotice({ type: "success", message: "You have signed out." });
    } catch (error) {
      setAuthNotice({
        type: "error",
        message: `Could not clear the signed-in session: ${getErrorMessage(error, "session storage is unavailable.")}`,
      });
    }
  };

  const submitProfile = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const errors: ProfileErrors = {};
    const name = profileName.trim();
    const address = profileAddress.trim();
    if (!name) errors.name = "Restaurant name is required.";
    else if (name.length > 150) errors.name = "Name cannot exceed 150 characters.";
    if (!address) errors.address = "Address is required.";
    else if (address.length > 300) errors.address = "Address cannot exceed 300 characters.";
    setProfileErrors(errors);
    if (Object.keys(errors).length || !validRestaurantId) return;

    setSavingProfile(true);
    setProfileNotice(null);
    try {
      const profile = await updateRestaurantProfile(restaurantId, name, address);
      setSettingsData((current) => current ? { ...current, profile } : current);
      setProfileNotice({ type: "success", message: "Restaurant profile saved." });
    } catch (error) {
      setProfileNotice({ type: "error", message: `Profile was not saved: ${getErrorMessage(error, "request failed.")}` });
    } finally {
      setSavingProfile(false);
    }
  };

  const submitCapacity = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!isPositiveInteger(defaultSeatCapacity)) {
      setCapacityError("Enter a positive whole number of seats.");
      return;
    }
    if (!validRestaurantId) return;

    setCapacityError(null);
    setCapacityNotice(null);
    setSavingCapacity(true);
    try {
      const settings = await saveRestaurantSettings(
        restaurantId,
        Number(defaultSeatCapacity),
        settingsData?.settings === null,
      );
      setSettingsData((current) => current ? { ...current, settings } : current);
      setCapacityNotice({ type: "success", message: "Default seat count saved." });
    } catch (error) {
      setCapacityNotice({ type: "error", message: `Seat count was not saved: ${getErrorMessage(error, "request failed.")}` });
    } finally {
      setSavingCapacity(false);
    }
  };

  const beginAddTable = () => {
    const nextNumber = Math.max(0, ...(settingsData?.tables ?? []).map((table) => table.tableNumber)) + 1;
    setEditingTableId(null);
    setTableForm({
      ...emptyTableForm,
      tableNumber: String(nextNumber),
      seatCount: defaultSeatCapacity,
    });
    setTableErrors({});
    setTableNotice(null);
    setEditorOpen(true);
  };

  const beginEditTable = (table: DiningTableResponse) => {
    setEditingTableId(table.id);
    setTableForm({
      tableNumber: String(table.tableNumber),
      seatCount: String(table.seatCount),
      isActive: table.isActive,
      staffMembershipId: table.assignment ? String(table.assignment.staffMembershipId) : "",
    });
    setTableErrors({});
    setTableNotice(null);
    setEditorOpen(true);
  };

  const cancelTableEdit = () => {
    setEditorOpen(false);
    setEditingTableId(null);
    setTableErrors({});
    setTableNotice(null);
  };

  const submitTable = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const errors: TableErrors = {};
    const tableNumberValid = isPositiveInteger(tableForm.tableNumber);
    const seatCountValid = isPositiveInteger(tableForm.seatCount);
    if (!tableNumberValid) errors.tableNumber = "Enter a positive whole table number.";
    if (!seatCountValid) errors.seatCount = "Enter a positive whole number of seats.";
    if (tableNumberValid && (settingsData?.tables ?? []).some(
      (table) => table.tableNumber === Number(tableForm.tableNumber) && table.id !== editingTableId,
    )) {
      errors.tableNumber = "This table number is already in use.";
    }
    if (!tableForm.isActive && tableForm.staffMembershipId) {
      errors.staffMembershipId = "Unassign the waiter before deactivating this table.";
    }
    setTableErrors(errors);
    if (Object.keys(errors).length || !validRestaurantId) return;

    setSavingTable(true);
    setTableNotice(null);
    const oldAssignment = settingsData?.tables.find((table) => table.id === editingTableId)?.assignment?.staffMembershipId ?? null;
    const nextAssignment = tableForm.staffMembershipId ? Number(tableForm.staffMembershipId) : null;
    const mustUnassignBeforeDeactivation = oldAssignment !== null && nextAssignment === null && !tableForm.isActive;
    let assignmentRemoved = false;
    try {
      if (mustUnassignBeforeDeactivation && editingTableId !== null) {
        try {
          await removeTableAssignment(restaurantId, editingTableId);
          assignmentRemoved = true;
        } catch (error) {
          setTableNotice({
            type: "error",
            message: `Table was not deactivated because the waiter could not be unassigned: ${getErrorMessage(error, "request failed.")}`,
          });
          return;
        }
      }

      const savedTable = await saveDiningTable(restaurantId, editingTableId, {
        tableNumber: Number(tableForm.tableNumber),
        seatCount: Number(tableForm.seatCount),
        isActive: tableForm.isActive,
      });

      if (nextAssignment !== oldAssignment && !assignmentRemoved) {
        try {
          if (nextAssignment === null) {
            await removeTableAssignment(restaurantId, savedTable.id);
          } else {
            await saveTableAssignment(restaurantId, savedTable.id, nextAssignment);
          }
        } catch (error) {
          setEditingTableId(savedTable.id);
          setTableNotice({
            type: "error",
            message: `Table details were saved, but the staff assignment was not changed: ${getErrorMessage(error, "request failed.")}`,
          });
          await refreshRestaurant();
          return;
        }
      }

      setEditorOpen(false);
      setEditingTableId(null);
      await refreshRestaurant();
      setTableNotice({ type: "success", message: "Table saved and list refreshed." });
    } catch (error) {
      setTableNotice({
        type: assignmentRemoved ? "warning" : "error",
        message: assignmentRemoved
          ? `The waiter was unassigned, but table details were not saved: ${getErrorMessage(error, "request failed.")}`
          : `Table was not saved: ${getErrorMessage(error, "request failed.")}`,
      });
      if (assignmentRemoved) await refreshRestaurant();
    } finally {
      setSavingTable(false);
    }
  };

  const profileChanged = settingsData !== null &&
    (profileName.trim() !== settingsData.profile.name || profileAddress.trim() !== settingsData.profile.address);
  const capacityChanged = settingsData !== null && (
    settingsData.settings === null
      ? defaultSeatCapacity !== "4"
      : defaultSeatCapacity !== String(settingsData.settings.defaultSeatCapacity)
  );
  const canSaveCapacity = settingsData !== null && (settingsData.settings === null || capacityChanged);

  return (
    <div className="app-shell admin-shell">
      <aside className="admin-sidebar">
        <Link className="admin-brand" to="/" aria-label="Plate restaurant customer menu">
          <strong>PLATE</strong>
          <span>RESTAURANT OPERATIONS</span>
        </Link>
        <div className="admin-sidebar-label">WORKSPACE</div>
        <Link className="admin-sidebar-link" to="/">Overview</Link>
        <Link className="admin-sidebar-link" to="/admin/dishes">Menu management</Link>
        <Link className="admin-sidebar-link" to="/admin/staff">Staff and access</Link>
        <span className="admin-sidebar-link active" aria-current="page">Settings</span>
        <div className="admin-profile">
          <span className="admin-avatar" aria-hidden="true">
            {(account?.userName?.slice(0, 2) || "AD").toUpperCase()}
          </span>
          <span><strong>{account?.userName || "Restaurant admin"}</strong><small>Restaurant settings</small></span>
        </div>
      </aside>

      <main className="page admin-page staff-page settings-page">
        <header className="admin-header">
          <div>
            <p className="eyebrow">ADMIN / SETTINGS</p>
            <h1>Restaurant settings</h1>
            <p className="subtitle">Manage restaurant details, table capacity, and waiter assignments.</p>
          </div>
          {account && (
            <div className="settings-header-actions">
              <button type="button" className="secondary-button" onClick={() => void refreshRestaurant()} disabled={loading || !validRestaurantId}>
                {loading ? "Refreshing..." : "Refresh"}
              </button>
              <button type="button" className="secondary-button" onClick={signOut}>Sign out</button>
            </div>
          )}
        </header>

        {sessionChecking ? (
          <section className="staff-card"><p className="staff-empty" role="status">Checking signed-in session...</p></section>
        ) : !account ? (
          <section className="staff-card settings-auth-card">
            <div className="staff-card-heading">
              <div><p className="eyebrow">ACCOUNT</p><h2>Sign in to manage settings</h2></div>
            </div>
            {authNotice && <p className={`staff-notice ${authNotice.type}`} role={authNotice.type === "error" ? "alert" : "status"}>{authNotice.message}</p>}
            <form className="staff-form" onSubmit={(event) => void submitLogin(event)}>
              <label>
                Username
                <input autoComplete="username" required value={username} onChange={(event) => setUsername(event.target.value)} />
              </label>
              <label>
                Password
                <input type="password" autoComplete="current-password" required value={password} onChange={(event) => setPassword(event.target.value)} />
              </label>
              <button type="submit" className="primary-button" disabled={authSubmitting}>
                {authSubmitting ? "Signing in..." : "Sign in"}
              </button>
            </form>
            <p className="staff-help">Use an administrator account for this restaurant.</p>
          </section>
        ) : !validRestaurantId ? (
          <section className="staff-card">
            <p className="staff-notice error" role="alert">This signed-in account has no valid restaurant ID. Sign in with an administrator membership.</p>
          </section>
        ) : (
          <>
            {pageError && (
              <p className="staff-notice error settings-page-error" role="alert">
                Could not load restaurant settings: {pageError}
                <button type="button" className="staff-text-button" onClick={() => void refreshRestaurant()} disabled={loading}>Try again</button>
              </p>
            )}
            <div className="settings-tabs" role="tablist" aria-label="Restaurant settings sections">
              <button type="button" role="tab" aria-selected={activeTab === "profile"} className={activeTab === "profile" ? "active" : ""} onClick={() => setActiveTab("profile")}>
                Restaurant profile
              </button>
              <button type="button" role="tab" aria-selected={activeTab === "tables"} className={activeTab === "tables" ? "active" : ""} onClick={() => setActiveTab("tables")}>
                Tables &amp; staff
              </button>
            </div>

            {loading && !settingsData ? (
              <section className="staff-card"><p className="staff-empty" role="status">Loading restaurant settings...</p></section>
            ) : settingsData && activeTab === "profile" ? (
              <section className="staff-card settings-card" aria-labelledby="profile-settings-title">
                <div className="staff-card-heading">
                  <div>
                    <p className="eyebrow">RESTAURANT PROFILE</p>
                    <h2 id="profile-settings-title">Restaurant details</h2>
                  </div>
                  <span className="settings-id">RESTAURANT #{restaurantId}</span>
                </div>
                {profileNotice && <p className={`staff-notice ${profileNotice.type}`} role={profileNotice.type === "error" ? "alert" : "status"}>{profileNotice.message}</p>}
                <form className="settings-profile-form" onSubmit={(event) => void submitProfile(event)} noValidate>
                  <label>
                    Restaurant name
                    <input
                      required
                      maxLength={150}
                      value={profileName}
                      aria-invalid={Boolean(profileErrors.name)}
                      aria-describedby={profileErrors.name ? "profile-name-error" : undefined}
                      onChange={(event) => { setProfileName(event.target.value); setProfileErrors((current) => ({ ...current, name: undefined })); }}
                    />
                    {profileErrors.name && <span id="profile-name-error" className="settings-field-error">{profileErrors.name}</span>}
                  </label>
                  <label>
                    Restaurant address
                    <input
                      required
                      maxLength={300}
                      value={profileAddress}
                      aria-invalid={Boolean(profileErrors.address)}
                      aria-describedby={profileErrors.address ? "profile-address-error" : undefined}
                      onChange={(event) => { setProfileAddress(event.target.value); setProfileErrors((current) => ({ ...current, address: undefined })); }}
                    />
                    {profileErrors.address && <span id="profile-address-error" className="settings-field-error">{profileErrors.address}</span>}
                  </label>
                  <div className="settings-form-actions">
                    <button type="button" className="secondary-button" disabled={savingProfile || !profileChanged} onClick={() => {
                      setProfileName(settingsData.profile.name);
                      setProfileAddress(settingsData.profile.address);
                      setProfileErrors({});
                      setProfileNotice(null);
                    }}>Cancel</button>
                    <button type="submit" className="primary-button" disabled={savingProfile || loading || !profileChanged}>
                      {savingProfile ? "Saving..." : "Save changes"}
                    </button>
                  </div>
                </form>
              </section>
            ) : settingsData && activeTab === "tables" ? (
              <div className="settings-sections">
                <section className="staff-card settings-card" aria-labelledby="seat-capacity-title">
                  <div className="staff-card-heading">
                    <div><p className="eyebrow">DEFAULT TABLE CAPACITY</p><h2 id="seat-capacity-title">Seats per new table</h2></div>
                  </div>
                  {settingsData.settings === null && <p className="staff-help">No default has been saved yet. Saving will create the restaurant settings.</p>}
                  {capacityNotice && <p className={`staff-notice ${capacityNotice.type}`} role={capacityNotice.type === "error" ? "alert" : "status"}>{capacityNotice.message}</p>}
                  <form className="settings-capacity-form" onSubmit={(event) => void submitCapacity(event)} noValidate>
                    <label>
                      Default seats
                      <input
                        type="number"
                        min="1"
                        max="2147483647"
                        step="1"
                        required
                        value={defaultSeatCapacity}
                        aria-invalid={Boolean(capacityError)}
                        aria-describedby={capacityError ? "capacity-error" : undefined}
                        onChange={(event) => { setDefaultSeatCapacity(event.target.value); setCapacityError(null); }}
                      />
                      {capacityError && <span id="capacity-error" className="settings-field-error">{capacityError}</span>}
                    </label>
                    <div className="settings-form-actions">
                      <button type="button" className="secondary-button" disabled={savingCapacity || !capacityChanged} onClick={() => {
                        setDefaultSeatCapacity(String(settingsData.settings?.defaultSeatCapacity ?? 4));
                        setCapacityError(null);
                        setCapacityNotice(null);
                      }}>Cancel</button>
                      <button type="submit" className="primary-button" disabled={savingCapacity || loading || !canSaveCapacity}>
                        {savingCapacity ? "Saving..." : "Save default"}
                      </button>
                    </div>
                  </form>
                </section>

                <section className="staff-card settings-card" aria-labelledby="dining-tables-title">
                  <div className="staff-card-heading">
                    <div><p className="eyebrow">FLOOR PLAN</p><h2 id="dining-tables-title">Dining tables</h2></div>
                    <button type="button" className="primary-button settings-add-table" onClick={beginAddTable} disabled={loading || editorOpen}>
                      + Add table
                    </button>
                  </div>
                  {tableNotice && <p className={`staff-notice ${tableNotice.type}`} role={tableNotice.type === "error" ? "alert" : "status"}>{tableNotice.message}</p>}

                  {editorOpen && (
                    <form className="settings-table-editor" onSubmit={(event) => void submitTable(event)} noValidate>
                      <div className="settings-editor-heading">
                        <div><p className="eyebrow">{editingTableId === null ? "NEW TABLE" : "EDIT TABLE"}</p><h3>{editingTableId === null ? "Add a dining table" : `Table #${tableForm.tableNumber}`}</h3></div>
                        <button type="button" className="staff-text-button" onClick={cancelTableEdit} disabled={savingTable}>Cancel</button>
                      </div>
                      <label>
                        Table number
                        <input
                          type="number" min="1" max="2147483647" step="1" required
                          value={tableForm.tableNumber}
                          aria-invalid={Boolean(tableErrors.tableNumber)}
                          aria-describedby={tableErrors.tableNumber ? "table-number-error" : undefined}
                          onChange={(event) => { setTableForm((current) => ({ ...current, tableNumber: event.target.value })); setTableErrors((current) => ({ ...current, tableNumber: undefined })); }}
                        />
                        {tableErrors.tableNumber && <span id="table-number-error" className="settings-field-error">{tableErrors.tableNumber}</span>}
                      </label>
                      <label>
                        Seat count
                        <input
                          type="number" min="1" max="2147483647" step="1" required
                          value={tableForm.seatCount}
                          aria-invalid={Boolean(tableErrors.seatCount)}
                          aria-describedby={tableErrors.seatCount ? "table-seat-error" : undefined}
                          onChange={(event) => { setTableForm((current) => ({ ...current, seatCount: event.target.value })); setTableErrors((current) => ({ ...current, seatCount: undefined })); }}
                        />
                        {tableErrors.seatCount && <span id="table-seat-error" className="settings-field-error">{tableErrors.seatCount}</span>}
                      </label>
                      <label>
                        Assigned waiter
                        <select
                          value={tableForm.staffMembershipId}
                          disabled={!tableForm.isActive}
                          aria-invalid={Boolean(tableErrors.staffMembershipId)}
                          aria-describedby={tableErrors.staffMembershipId ? "table-assignment-error" : undefined}
                          onChange={(event) => { setTableForm((current) => ({ ...current, staffMembershipId: event.target.value })); setTableErrors((current) => ({ ...current, staffMembershipId: undefined })); }}
                        >
                          <option value="">Unassigned</option>
                          {activeWaiters.map((member) => (
                            <option key={member.id} value={member.id}>{member.userName}</option>
                          ))}
                          {editingTableId !== null && (() => {
                            const currentAssignment = settingsData.tables.find((table) => table.id === editingTableId)?.assignment;
                            if (!currentAssignment || activeWaiters.some((member) => member.id === currentAssignment.staffMembershipId)) return null;
                            return <option value={currentAssignment.staffMembershipId} disabled>{currentAssignment.userName || "Inactive waiter"} (inactive)</option>;
                          })()}
                        </select>
                        {tableErrors.staffMembershipId && <span id="table-assignment-error" className="settings-field-error">{tableErrors.staffMembershipId}</span>}
                        {activeWaiters.length === 0 && <span className="settings-field-hint">No active waiters are available to assign.</span>}
                      </label>
                      <label className="settings-active-toggle">
                        <input
                          type="checkbox"
                          checked={tableForm.isActive}
                          onChange={(event) => setTableForm((current) => ({
                            ...current,
                            isActive: event.target.checked,
                            staffMembershipId: event.target.checked ? current.staffMembershipId : "",
                          }))}
                        />
                        Active for service
                      </label>
                      <div className="settings-form-actions">
                        <button type="button" className="secondary-button" onClick={cancelTableEdit} disabled={savingTable}>Cancel</button>
                        <button type="submit" className="primary-button" disabled={savingTable}>
                          {savingTable ? "Saving..." : "Save table"}
                        </button>
                      </div>
                    </form>
                  )}

                  {loading && !settingsData.tables.length ? (
                    <p className="staff-empty" role="status">Loading tables...</p>
                  ) : settingsData.tables.length === 0 ? (
                    <div className="settings-empty">
                      <strong>No tables configured yet</strong>
                      <p>Add a table to configure its seat count and waiter assignment.</p>
                      {!editorOpen && <button type="button" className="secondary-button" onClick={beginAddTable}>Add first table</button>}
                    </div>
                  ) : (
                    <div className="staff-table-wrap">
                      <table className="staff-table settings-table">
                        <thead>
                          <tr><th scope="col">Table</th><th scope="col">Seats</th><th scope="col">Waiter</th><th scope="col">Status</th><th scope="col">Action</th></tr>
                        </thead>
                        <tbody>
                          {settingsData.tables.map((table) => (
                            <tr key={table.id}>
                              <td><strong>Table {table.tableNumber}</strong></td>
                              <td>{table.seatCount} seats</td>
                              <td>{table.assignment?.userName || <span className="settings-unassigned">Unassigned</span>}</td>
                              <td><span className={`settings-table-status ${table.isActive ? "active" : "inactive"}`}>{table.isActive ? "Active" : "Inactive"}</span></td>
                              <td><button type="button" className="staff-text-button" onClick={() => beginEditTable(table)} disabled={savingTable}>Edit</button></td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  )}
                  {settingsData.tables.length > 0 && activeWaiters.length === 0 && (
                    <p className="staff-help">Create or activate a staff membership with the Waiter role to assign table coverage.</p>
                  )}
                </section>
              </div>
            ) : null}
          </>
        )}
      </main>
    </div>
  );
}

export default RestaurantSettingsPage;
