import { useEffect, useState } from "react";
import type { FormEvent } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import {
  acceptInvitation,
  changeStaffRole,
  clearStoredAccessToken,
  createRestaurant,
  getCurrentAccount,
  getRestaurantProfile,
  getRestaurantStaff,
  getStoredAccessToken,
  inviteStaff,
  login,
  revokeStaffInvitation,
  setStaffMembershipStatus,
  storeAccessToken,
  updateRestaurantProfile,
  type AcceptedInvitationResponse,
  type AccountResponse,
  type RestaurantResponse,
  type StaffInvitationResponse,
  type StaffMemberResponse,
} from "./services/api";
import { ApiError } from "./services/api";
import { RestaurantConfigurationPanel } from "./RestaurantConfigurationPanel";

const staffRoles = [
  "Administrator",
  "Waiter",
  "Chef",
  "Reception Manager",
] as const;

export function getStaffRouteForRole(role?: string | null): string {
  switch (role?.trim().toLowerCase()) {
    case "waiter":
      return "/waiter/orders";
    case "chef":
      return "/chef/orders";
    case "administrator":
    case "reception manager":
      return "/admin/staff";
    default:
      return "";
  }
}

type Notice = {
  type: "success" | "error" | "warning";
  message: string;
};

function getErrorMessage(error: unknown, fallback: string): string {
  return error instanceof Error ? error.message : fallback;
}

function getMembershipStatus(status: number | string): "Active" | "Revoked" {
  const normalized = String(status).toLowerCase();
  return status === 1 || normalized === "1" || normalized === "active" ? "Active" : "Revoked";
}

function getInvitationStatus(status: number | string): "Pending" | "Accepted" | "Revoked" {
  const normalized = String(status).toLowerCase();
  if (status === 1 || normalized === "1" || normalized === "pending") return "Pending";
  if (status === 2 || normalized === "2" || normalized === "accepted") return "Accepted";
  return "Revoked";
}

function AccountPanel({
  accessToken,
  account,
  onLogin,
  onLogout,
  authNotice,
}: {
  accessToken: string | null;
  account: AccountResponse | null;
  onLogin: (username: string, password: string) => Promise<void>;
  onLogout: () => void;
  authNotice: Notice | null;
}) {
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [submitting, setSubmitting] = useState(false);

  const submitLogin = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setSubmitting(true);
    try {
      await onLogin(username.trim(), password);
      setPassword("");
    } catch {
      // The parent displays the API error in the account panel.
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <section className="staff-card" aria-labelledby="account-title">
      <div className="staff-card-heading">
        <div>
          <p className="eyebrow">ACCOUNT</p>
          <h2 id="account-title">{account ? "Signed in" : "Sign in"}</h2>
        </div>
        {account && (
          <button type="button" className="secondary-button" onClick={onLogout}>
            Sign out
          </button>
        )}
      </div>

      {authNotice && (
        <p className={`staff-notice ${authNotice.type}`} role={authNotice.type === "error" ? "alert" : "status"}>
          {authNotice.message}
        </p>
      )}

      {account ? (
      <>
        <dl className="staff-account-details">
          <div><dt>Username</dt><dd>{account.userName}</dd></div>
          <div><dt>Email</dt><dd>{account.email || "Not provided"}</dd></div>
          <div><dt>Session</dt><dd>{accessToken ? "Authenticated" : "Signed out"}</dd></div>
          {account.role && <div><dt>Restaurant role</dt><dd>{account.role}</dd></div>}
          {account.restaurantId && <div><dt>Restaurant</dt><dd>#{account.restaurantId}</dd></div>}
        </dl>
        {account.role === "Chef" && (
          <>
            <Link className="secondary-button staff-workspace-link" to="/chef/availability">
              Open Chef availability
            </Link>
            <Link className="secondary-button staff-workspace-link" to="/chef/orders">
              Open kitchen orders
            </Link>
          </>
        )}
        {account.role === "Waiter" && (
          <Link className="secondary-button staff-workspace-link" to="/waiter/orders">
            Open waiter orders
          </Link>
        )}
      </>
      ) : (
        <form className="staff-form" onSubmit={(event) => void submitLogin(event)}>
          <label>
            Username
            <input
              autoComplete="username"
              required
              value={username}
              onChange={(event) => setUsername(event.target.value)}
            />
          </label>
          <label>
            Password
            <input
              type="password"
              autoComplete="current-password"
              required
              value={password}
              onChange={(event) => setPassword(event.target.value)}
            />
          </label>
          <button type="submit" className="primary-button" disabled={submitting}>
            {submitting ? "Signing in..." : "Sign in"}
          </button>
        </form>
      )}
    </section>
  );
}

function StaffLoginPage() {
  const navigate = useNavigate();
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [checkingSession, setCheckingSession] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let active = true;
    const token = getStoredAccessToken();
    if (!token) {
      setCheckingSession(false);
      return () => {
        active = false;
      };
    }

    void getCurrentAccount()
      .then((account) => {
        if (!active) return;
        const route = getStaffRouteForRole(account.role);
        if (route) {
          navigate(route, { replace: true });
        } else {
          clearStoredAccessToken();
          setError("This account does not have a supported staff role.");
          setCheckingSession(false);
        }
      })
      .catch(() => {
        if (!active) return;
        clearStoredAccessToken();
        setCheckingSession(false);
      });

    return () => {
      active = false;
    };
  }, [navigate]);

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setSubmitting(true);
    setError(null);
    try {
      const response = await login(username.trim(), password);
      if (!response.accessToken) {
        throw new Error("The API did not return an access token.");
      }

      storeAccessToken(response.accessToken);
      const account = await getCurrentAccount();
      const route = getStaffRouteForRole(account.role);
      if (!route) {
        clearStoredAccessToken();
        throw new Error("This account does not have a supported staff role.");
      }

      navigate(route, { replace: true });
    } catch (caughtError) {
      clearStoredAccessToken();
      setError(getErrorMessage(caughtError, "Could not sign in."));
    } finally {
      setSubmitting(false);
    }
  };

  if (checkingSession) {
    return (
      <div className="auth-state-panel" role="status">
        <p>Checking your session...</p>
      </div>
    );
  }

  return (
    <main className="staff-login-page">
      <section className="staff-login-intro">
        <Link className="staff-login-brand" to="/" aria-label="Plate Restaurant home">
          <span className="staff-login-mark">P</span>
          <span>PLATE</span>
        </Link>
        <div className="staff-login-message">
          <p className="staff-login-kicker">RESTAURANT OPERATIONS</p>
          <h1>Your team,<br />working in sync.</h1>
          <p>One place for your floor and kitchen teams to stay connected and keep service moving.</p>
          <div className="staff-login-preview" aria-hidden="true">
            <div className="staff-login-preview-card">
              <span className="preview-chip">FLOOR</span>
              <span className="preview-line wide" />
              <span className="preview-line" />
              <span className="preview-table"><i /><i /><i /></span>
            </div>
            <div className="staff-login-preview-card preview-kitchen">
              <span className="preview-chip">KITCHEN</span>
              <span className="preview-line wide" />
              <span className="preview-line" />
              <span className="preview-order"><i /><i /></span>
            </div>
          </div>
        </div>
        <p className="staff-login-footnote">A calmer service starts with a connected team.</p>
      </section>

      <section className="staff-login-form-side">
        <div className="staff-login-form-wrap">
          <p className="staff-login-kicker">STAFF ACCESS</p>
          <h2>Welcome back</h2>
          <p className="staff-login-subtitle">Sign in to continue to your workspace.</p>
          <form className="staff-form staff-login-form" onSubmit={(event) => void submit(event)}>
            <label>
              Username
              <input
                autoComplete="username"
                required
                value={username}
                onChange={(event) => setUsername(event.target.value)}
              />
            </label>
            <label>
              Password
              <input
                type="password"
                autoComplete="current-password"
                required
                value={password}
                onChange={(event) => setPassword(event.target.value)}
              />
            </label>
            {error && <p className="staff-notice error" role="alert">{error}</p>}
            <button type="submit" className="primary-button" disabled={submitting}>
              {submitting ? "Signing in..." : "Sign in"}
            </button>
          </form>
          <Link className="staff-login-home-link" to="/">Return to public menu</Link>
        </div>
      </section>
    </main>
  );
}

function CreateRestaurantPanel({
  onCreated,
}: {
  onCreated: (restaurantId: number) => Promise<void>;
}) {
  const [name, setName] = useState("");
  const [address, setAddress] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [notice, setNotice] = useState<Notice | null>(null);

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setSubmitting(true);
    setNotice(null);
    try {
      const restaurant = await createRestaurant(name.trim(), address.trim());
      await onCreated(restaurant.id);
      setName("");
      setAddress("");
      setNotice({
        type: "success",
        message: `${restaurant.name} was created. You are its administrator (restaurant #${restaurant.id}).`,
      });
    } catch (error) {
      setNotice({ type: "error", message: getErrorMessage(error, "Could not create the restaurant.") });
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <details className="staff-card staff-collapsible">
      <summary>
        <span><strong>Create a restaurant</strong><small>For accounts with restaurant-creation access.</small></span>
      </summary>
      <form className="staff-form" onSubmit={(event) => void submit(event)}>
        <label>
          Restaurant name
          <input required maxLength={150} value={name} onChange={(event) => setName(event.target.value)} />
        </label>
        <label>
          Address
          <input required maxLength={300} value={address} onChange={(event) => setAddress(event.target.value)} />
        </label>
        <button type="submit" className="primary-button" disabled={submitting}>
          {submitting ? "Creating..." : "Create restaurant"}
        </button>
        {notice && <p className={`staff-notice ${notice.type}`} role={notice.type === "error" ? "alert" : "status"}>{notice.message}</p>}
      </form>
    </details>
  );
}

function RestaurantProfilePanel({
  profile,
  loading,
  notice,
  onRefresh,
  onSave,
}: {
  profile: RestaurantResponse | null;
  loading: boolean;
  notice: Notice | null;
  onRefresh: () => void;
  onSave: (name: string, address: string) => Promise<void>;
}) {
  const [name, setName] = useState("");
  const [address, setAddress] = useState("");
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    if (profile) {
      setName(profile.name);
      setAddress(profile.address);
    }
  }, [profile]);

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setSaving(true);
    try {
      await onSave(name.trim(), address.trim());
    } catch {
      // The parent displays the API error in the profile panel.
    } finally {
      setSaving(false);
    }
  };

  return (
    <section className="staff-card" aria-labelledby="restaurant-profile-title">
      <div className="staff-card-heading">
        <div>
          <p className="eyebrow">RESTAURANT PROFILE</p>
          <h2 id="restaurant-profile-title">Restaurant details</h2>
        </div>
        <button type="button" className="secondary-button" onClick={onRefresh} disabled={loading || saving}>
          {loading ? "Loading..." : "Refresh"}
        </button>
      </div>
      {notice && <p className={`staff-notice ${notice.type}`} role={notice.type === "error" ? "alert" : "status"}>{notice.message}</p>}
      {loading && !profile ? (
        <p className="staff-empty" role="status">Loading restaurant profile...</p>
      ) : profile ? (
        <form className="staff-profile-form" onSubmit={(event) => void submit(event)}>
          <label>
            Restaurant name
            <input required maxLength={150} value={name} onChange={(event) => setName(event.target.value)} />
          </label>
          <label>
            Address
            <input required maxLength={300} value={address} onChange={(event) => setAddress(event.target.value)} />
          </label>
          <button type="submit" className="primary-button" disabled={saving || loading}>
            {saving ? "Saving..." : "Save profile"}
          </button>
        </form>
      ) : (
        <p className="staff-empty" role="status">Restaurant profile is unavailable.</p>
      )}
    </section>
  );
}

function StaffDirectory({
  restaurantId,
  members,
  loading,
  notice,
  drafts,
  busyMemberId,
  onRefresh,
  onRoleDraft,
  onSaveRole,
  onToggleStatus,
}: {
  restaurantId: number;
  members: StaffMemberResponse[];
  loading: boolean;
  notice: Notice | null;
  drafts: Record<number, string>;
  busyMemberId: number | null;
  onRefresh: () => void;
  onRoleDraft: (memberId: number, role: string) => void;
  onSaveRole: (member: StaffMemberResponse) => void;
  onToggleStatus: (member: StaffMemberResponse) => void;
}) {
  return (
    <section className="staff-card" aria-labelledby="staff-directory-title">
      <div className="staff-card-heading">
        <div>
          <p className="eyebrow">RESTAURANT #{restaurantId}</p>
          <h2 id="staff-directory-title">Staff and roles</h2>
        </div>
        <button type="button" className="secondary-button" onClick={onRefresh} disabled={loading}>
          {loading ? "Refreshing..." : "Refresh"}
        </button>
      </div>

      {notice && <p className={`staff-notice ${notice.type}`} role={notice.type === "error" ? "alert" : "status"}>{notice.message}</p>}
      {loading ? (
        <p className="staff-empty" role="status">Loading current memberships...</p>
      ) : members.length === 0 ? (
        <p className="staff-empty" role="status">No staff memberships were returned for this restaurant.</p>
      ) : (
        <div className="staff-table-wrap">
          <table className="staff-table">
            <thead>
              <tr>
                <th scope="col">Employee</th>
                <th scope="col">Role</th>
                <th scope="col">Membership</th>
                <th scope="col">Actions</th>
              </tr>
            </thead>
            <tbody>
              {members.map((member) => {
                const status = getMembershipStatus(member.status);
                const selectedRole = drafts[member.id] ?? member.role;
                const busy = busyMemberId === member.id;
                return (
                  <tr key={member.id}>
                    <td>
                      <strong>{member.userName}</strong>
                      <small>{member.email || member.userId}</small>
                    </td>
                    <td>
                      <div className="staff-role-control">
                        <select
                          aria-label={`Role for ${member.userName}`}
                          value={selectedRole}
                          disabled={loading || busy || status !== "Active"}
                          onChange={(event) => onRoleDraft(member.id, event.target.value)}
                        >
                          {staffRoles.map((role) => <option key={role} value={role}>{role}</option>)}
                        </select>
                        <button
                          type="button"
                          className="staff-text-button"
                          disabled={loading || busy || status !== "Active" || selectedRole === member.role}
                          onClick={() => onSaveRole(member)}
                        >
                          {busy ? "Saving..." : "Save role"}
                        </button>
                      </div>
                    </td>
                    <td><span className={`staff-status ${status.toLowerCase()}`}>{status}</span></td>
                    <td>
                      <button
                        type="button"
                        className="staff-text-button"
                        disabled={loading || busy}
                        onClick={() => onToggleStatus(member)}
                      >
                        {busy ? "Saving..." : status === "Active" ? "Revoke access" : "Reactivate"}
                      </button>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}

function StaffManagementPage() {
  const navigate = useNavigate();
  const [accessToken, setAccessToken] = useState<string | null>(null);
  const [account, setAccount] = useState<AccountResponse | null>(null);
  const [authNotice, setAuthNotice] = useState<Notice | null>(null);
  const [restaurantIdInput, setRestaurantIdInput] = useState("");
  const [restaurantId, setRestaurantId] = useState<number | null>(null);
  const [restaurantProfile, setRestaurantProfile] = useState<RestaurantResponse | null>(null);
  const [profileLoading, setProfileLoading] = useState(false);
  const [profileNotice, setProfileNotice] = useState<Notice | null>(null);
  const [members, setMembers] = useState<StaffMemberResponse[]>([]);
  const [staffLoading, setStaffLoading] = useState(false);
  const [staffNotice, setStaffNotice] = useState<Notice | null>(null);
  const [roleDrafts, setRoleDrafts] = useState<Record<number, string>>({});
  const [busyMemberId, setBusyMemberId] = useState<number | null>(null);
  const [inviteEmail, setInviteEmail] = useState("");
  const [inviteRole, setInviteRole] = useState<string>("Waiter");
  const [inviting, setInviting] = useState(false);
  const [inviteNotice, setInviteNotice] = useState<Notice | null>(null);
  const [lastInvitation, setLastInvitation] = useState<StaffInvitationResponse | null>(null);
  const [revokingInvitation, setRevokingInvitation] = useState(false);

  useEffect(() => {
    try {
      const token = getStoredAccessToken();
      if (token) setAccessToken(token);
    } catch (error) {
      setAuthNotice({
        type: "error",
        message: `Could not read the signed-in session: ${getErrorMessage(error, "session storage is unavailable.")}`,
      });
    }
  }, []);

  useEffect(() => {
    if (!accessToken) {
      setAccount(null);
      return;
    }

    let active = true;
    void getCurrentAccount()
      .then((currentAccount) => {
        if (active) {
          setAccount(currentAccount);
          setAuthNotice(null);
          const staffRoute = getStaffRouteForRole(currentAccount.role);
          if (staffRoute && staffRoute !== "/admin/staff") {
            navigate(staffRoute, { replace: true });
          }
        }
      })
      .catch((error: unknown) => {
        if (active) {
          setAccount(null);
          setAuthNotice({
            type: "error",
            message: `Could not verify the signed-in account: ${getErrorMessage(error, "the session is invalid or expired.")}`,
          });
        }
      });

    return () => { active = false; };
  }, [accessToken, navigate]);

  const onLogin = async (username: string, password: string) => {
    setAuthNotice(null);
    try {
      const response = await login(username, password);
      if (!response.accessToken) {
        throw new Error("The API did not return an access token.");
      }
      storeAccessToken(response.accessToken);
      setAccessToken(response.accessToken);
      const currentAccount = await getCurrentAccount();
      setAccount(currentAccount);
      setAuthNotice({ type: "success", message: `Signed in as ${currentAccount.userName}.` });
      const route = getStaffRouteForRole(currentAccount.role);
      if (route && route !== "/admin/staff") {
        navigate(route, { replace: true });
      }
    } catch (error) {
      clearStoredAccessToken();
      setAccessToken(null);
      setAccount(null);
      setAuthNotice({ type: "error", message: getErrorMessage(error, "Could not sign in.") });
      throw error;
    }
  };

  const onLogout = () => {
    try {
      clearStoredAccessToken();
      setAccessToken(null);
      setAccount(null);
      setMembers([]);
      setRestaurantId(null);
      setRestaurantProfile(null);
      setProfileNotice(null);
      setRestaurantIdInput("");
      setRoleDrafts({});
      setStaffNotice(null);
      setInviteNotice(null);
      setLastInvitation(null);
      setAuthNotice({ type: "success", message: "You have signed out." });
    } catch (error) {
      setAuthNotice({
        type: "error",
        message: `Could not clear the signed-in session: ${getErrorMessage(error, "session storage is unavailable.")}`,
      });
    }
  };

  const refreshStaff = async (id = restaurantId) => {
    if (!id) return;
    setStaffLoading(true);
    setStaffNotice(null);
    try {
      const updatedMembers = await getRestaurantStaff(id);
      setMembers(updatedMembers);
      setRoleDrafts({});
      setRestaurantId(id);
      setRestaurantIdInput(String(id));
    } catch (error) {
      setStaffNotice({ type: "error", message: getErrorMessage(error, "Could not refresh restaurant memberships.") });
      throw error;
    } finally {
      setStaffLoading(false);
    }
  };

  const refreshProfile = async (id = restaurantId) => {
    if (!id) return;
    setProfileLoading(true);
    setProfileNotice(null);
    try {
      setRestaurantProfile(await getRestaurantProfile(id));
    } catch (error) {
      setRestaurantProfile(null);
      setProfileNotice({ type: "error", message: getErrorMessage(error, "Could not load the restaurant profile.") });
      throw error;
    } finally {
      setProfileLoading(false);
    }
  };

  const loadRestaurant = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const id = Number(restaurantIdInput);
    if (!Number.isSafeInteger(id) || id <= 0) {
      setStaffNotice({ type: "error", message: "Enter a valid positive restaurant ID." });
      return;
    }
    try {
      await Promise.all([refreshStaff(id), refreshProfile(id)]);
      setStaffNotice({ type: "success", message: `Memberships refreshed from the server for restaurant #${id}.` });
    } catch {
      // Each panel reports its own API error.
    }
  };

  const onRestaurantCreated = async (id: number) => {
    setRestaurantIdInput(String(id));
    try {
      await Promise.all([refreshStaff(id), refreshProfile(id)]);
    } catch {
      setStaffNotice({
        type: "warning",
        message: `Restaurant #${id} was created, but its memberships could not be refreshed. Use Refresh to check the server state.`,
      });
    }
  };

  const saveProfile = async (name: string, address: string) => {
    if (!restaurantId) return;
    if (!name || !address) {
      setProfileNotice({ type: "error", message: "Restaurant name and address are required." });
      return;
    }
    setProfileNotice(null);
    try {
      setRestaurantProfile(await updateRestaurantProfile(restaurantId, name, address));
      setProfileNotice({ type: "success", message: "Restaurant profile updated." });
    } catch (error) {
      setProfileNotice({ type: "error", message: `Profile was not updated: ${getErrorMessage(error, "request failed.")}` });
      throw error;
    }
  };

  const updateRestaurantIdInput = (value: string) => {
    setRestaurantIdInput(value);
    if (Number(value) !== restaurantId) {
      setRestaurantId(null);
      setMembers([]);
      setRestaurantProfile(null);
      setProfileNotice(null);
      setRoleDrafts({});
      setLastInvitation(null);
      setStaffNotice(null);
      setInviteNotice(null);
    }
  };

  const submitInvite = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!restaurantId) {
      setInviteNotice({ type: "error", message: "Load a restaurant before sending an invitation." });
      return;
    }

    setInviting(true);
    setInviteNotice(null);
    setLastInvitation(null);
    try {
      const invitation = await inviteStaff(restaurantId, inviteEmail.trim(), inviteRole);
      setLastInvitation(invitation);
      setInviteEmail("");
      setInviteNotice({
        type: "success",
        message: `Invitation sent to ${invitation.email}; status is ${getInvitationStatus(invitation.status).toLowerCase()}.`,
      });
    } catch (error) {
      const message = getErrorMessage(error, "Could not send the invitation.");
      setInviteNotice({
        type: "error",
        message: error instanceof ApiError && error.status === 409
          ? `Invitation was not sent: ${message}`
          : error instanceof ApiError && error.status === 503
            ? `Invitation delivery failed; no invitation is pending. ${message}`
            : message,
      });
    } finally {
      setInviting(false);
    }
  };

  const revokeInvitation = async () => {
    if (!restaurantId || !lastInvitation) return;
    setRevokingInvitation(true);
    setInviteNotice(null);
    try {
      await revokeStaffInvitation(restaurantId, lastInvitation.id);
      setLastInvitation({ ...lastInvitation, status: "Revoked" });
      setInviteNotice({ type: "success", message: `Pending invitation for ${lastInvitation.email} was revoked.` });
    } catch (error) {
      setInviteNotice({ type: "error", message: `Invitation was not revoked: ${getErrorMessage(error, "request failed.")}` });
    } finally {
      setRevokingInvitation(false);
    }
  };

  const saveRole = async (member: StaffMemberResponse) => {
    if (!restaurantId) return;
    const role = roleDrafts[member.id] ?? member.role;
    if (role === member.role) return;
    setBusyMemberId(member.id);
    setStaffNotice(null);
    try {
      await changeStaffRole(restaurantId, member.id, role);
      try {
        await refreshStaff(restaurantId);
        setStaffNotice({ type: "success", message: `${member.userName}'s role is now ${role}. Memberships were refreshed from the server.` });
      } catch {
        setStaffNotice({ type: "warning", message: `${member.userName}'s role was changed to ${role}, but refreshing memberships failed. Use Refresh to verify the current server state.` });
      }
    } catch (error) {
      setStaffNotice({ type: "error", message: `Role was not changed: ${getErrorMessage(error, "request failed.")}` });
    } finally {
      setBusyMemberId(null);
    }
  };

  const toggleMembershipStatus = async (member: StaffMemberResponse) => {
    if (!restaurantId) return;
    const wasActive = getMembershipStatus(member.status) === "Active";
    setBusyMemberId(member.id);
    setStaffNotice(null);
    try {
      await setStaffMembershipStatus(restaurantId, member.id, !wasActive);
      const nextState = wasActive ? "revoked" : "active";
      try {
        await refreshStaff(restaurantId);
        setStaffNotice({ type: "success", message: `${member.userName}'s membership is ${nextState}. The current membership list was refreshed.` });
      } catch {
        setStaffNotice({ type: "warning", message: `${member.userName}'s membership was ${nextState}, but refreshing the list failed. Use Refresh to verify the current server state.` });
      }
    } catch (error) {
      setStaffNotice({ type: "error", message: `Membership was not changed: ${getErrorMessage(error, "request failed.")}` });
    } finally {
      setBusyMemberId(null);
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
        <Link className="admin-sidebar-link" to="/">Overview</Link>
        <Link className="admin-sidebar-link" to="/admin/dishes">Menu management</Link>
        <span className="admin-sidebar-link active" aria-current="page">Staff and access</span>
        <Link className="admin-sidebar-link" to="/admin/settings">Settings</Link>
        <div className="admin-profile">
          <span className="admin-avatar" aria-hidden="true">
            {(account?.userName?.slice(0, 2) || "ST").toUpperCase()}
          </span>
          <span><strong>{account?.userName || "Signed out"}</strong><small>Account and permissions</small></span>
        </div>
      </aside>

      <main className="page admin-page staff-page">
        <header className="admin-header">
          <div>
            <p className="eyebrow">ADMIN / STAFF AND ACCESS</p>
            <h1>Accounts and roles</h1>
            <p className="subtitle">Manage sign-in, invitations, and restaurant staff permissions.</p>
          </div>
        </header>

        <AccountPanel
          accessToken={accessToken}
          account={account}
          onLogin={onLogin}
          onLogout={onLogout}
          authNotice={authNotice}
        />

        {account && (
          <>
            <CreateRestaurantPanel onCreated={onRestaurantCreated} />

            <section className="staff-card" aria-labelledby="restaurant-select-title">
              <div className="staff-card-heading">
                <div>
                  <p className="eyebrow">MEMBERSHIPS</p>
                  <h2 id="restaurant-select-title">Choose a restaurant</h2>
                </div>
              </div>
              <form className="staff-restaurant-form" onSubmit={(event) => void loadRestaurant(event)}>
                <label>
                  Restaurant ID
                  <input
                    type="number"
                    min="1"
                    required
                    value={restaurantIdInput}
                    onChange={(event) => updateRestaurantIdInput(event.target.value)}
                    placeholder="Enter an ID or create a restaurant above"
                  />
                </label>
                <button type="submit" className="primary-button" disabled={staffLoading}>
                  {staffLoading ? "Loading..." : "Load staff"}
                </button>
              </form>
              <p className="staff-help">The API does not expose a restaurant list. Use an existing restaurant ID or create one with an account that has permission.</p>
            </section>

            {restaurantId && (
              <>
                <RestaurantProfilePanel
                  profile={restaurantProfile}
                  loading={profileLoading}
                  notice={profileNotice}
                  onRefresh={() => void refreshProfile()}
                  onSave={saveProfile}
                />
                <RestaurantConfigurationPanel key={restaurantId} restaurantId={restaurantId} members={members} />
                <section className="staff-card" aria-labelledby="invite-title">
                  <div className="staff-card-heading">
                    <div>
                      <p className="eyebrow">INVITATIONS</p>
                      <h2 id="invite-title">Invite an employee</h2>
                    </div>
                  </div>
                  <form className="staff-invite-form" onSubmit={(event) => void submitInvite(event)}>
                    <label>
                      Employee email
                      <input
                        type="email"
                        autoComplete="email"
                        required
                        maxLength={256}
                        value={inviteEmail}
                        onChange={(event) => setInviteEmail(event.target.value)}
                      />
                    </label>
                    <label>
                      Restaurant role
                      <select value={inviteRole} onChange={(event) => setInviteRole(event.target.value)}>
                        {staffRoles.map((role) => (
                          <option key={role} value={role}>{role}</option>
                        ))}
                      </select>
                    </label>
                    <button type="submit" className="primary-button" disabled={inviting}>
                      {inviting ? "Sending..." : "Send invitation"}
                    </button>
                  </form>
                  {inviteNotice && <p className={`staff-notice ${inviteNotice.type}`} role={inviteNotice.type === "error" ? "alert" : "status"}>{inviteNotice.message}</p>}
                  {lastInvitation && (
                    <div className="staff-invitation-result">
                      <span>
                        {lastInvitation.email} · {lastInvitation.role} · {getInvitationStatus(lastInvitation.status)}
                      </span>
                      {lastInvitation.developmentToken && (
                        <Link to={`/invitations/accept?token=${encodeURIComponent(lastInvitation.developmentToken)}`}>
                          Open development acceptance link
                        </Link>
                      )}
                      {getInvitationStatus(lastInvitation.status) === "Pending" && (
                        <button type="button" className="staff-text-button" disabled={revokingInvitation} onClick={() => void revokeInvitation()}>
                          {revokingInvitation ? "Revoking..." : "Revoke pending invitation"}
                        </button>
                      )}
                    </div>
                  )}
                  <p className="staff-help">Invitations expire after 48 hours. Duplicate pending invitations and delivery failures are reported without displaying a false success.</p>
                </section>

                <StaffDirectory
                  restaurantId={restaurantId}
                  members={members}
                  loading={staffLoading}
                  notice={staffNotice}
                  drafts={roleDrafts}
                  busyMemberId={busyMemberId}
                  onRefresh={() => void refreshStaff()}
                  onRoleDraft={(memberId, role) => setRoleDrafts((current) => ({ ...current, [memberId]: role }))}
                  onSaveRole={(member) => void saveRole(member)}
                  onToggleStatus={(member) => void toggleMembershipStatus(member)}
                />
              </>
            )}
          </>
        )}

        <p className="staff-page-back"><Link to="/admin/dishes">Back to menu management</Link></p>
      </main>
    </div>
  );
}

function AcceptInvitationPage() {
  const [searchParams] = useSearchParams();
  const [token, setToken] = useState(searchParams.get("token") ?? "");
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [result, setResult] = useState<AcceptedInvitationResponse | null>(null);
  const [notice, setNotice] = useState<Notice | null>(null);

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setSubmitting(true);
    setNotice(null);
    setResult(null);

    const request: { token: string; username?: string; password?: string } = {
      token: token.trim(),
    };
    if (username.trim() || password) {
      if (!username.trim() || !password) {
        setNotice({ type: "error", message: "Enter both a username and password to create a new account." });
        setSubmitting(false);
        return;
      }
      request.username = username.trim();
      request.password = password;
    }

    try {
      const accepted = await acceptInvitation(request);
      setResult(accepted);
      setPassword("");
      setNotice({
        type: "success",
        message: `Invitation accepted. You are now a ${accepted.role} member of restaurant #${accepted.restaurantId}.`,
      });
    } catch (error) {
      const message = getErrorMessage(error, "Could not accept this invitation.");
      setNotice({
        type: "error",
        message: error instanceof ApiError && error.status === 409
          ? `This invitation cannot be accepted because the account is already active. ${message}`
          : message,
      });
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="app-shell staff-accept-shell">
      <main className="staff-accept-page">
        <Link className="staff-back-link" to="/admin/staff">← Staff and access</Link>
        <section className="staff-card">
          <p className="eyebrow">RESTAURANT STAFF</p>
          <h1>Accept invitation</h1>
          <p className="staff-help">If you already have an account for the invited email, sign out of any other account and sign in as the invited user under Staff and access before accepting. Otherwise, create an account below.</p>
          <form className="staff-form" onSubmit={(event) => void submit(event)}>
            <label>
              Invitation token
              <input required maxLength={200} value={token} onChange={(event) => setToken(event.target.value)} />
            </label>
            <label>
              New username <span className="staff-field-hint">(only for a new account)</span>
              <input autoComplete="username" value={username} onChange={(event) => setUsername(event.target.value)} />
            </label>
            <label>
              New password <span className="staff-field-hint">(only for a new account)</span>
              <input type="password" autoComplete="new-password" minLength={10} value={password} onChange={(event) => setPassword(event.target.value)} />
            </label>
            {notice && <p className={`staff-notice ${notice.type}`} role={notice.type === "error" ? "alert" : "status"}>{notice.message}</p>}
            <button type="submit" className="primary-button" disabled={submitting}>
              {submitting ? "Accepting..." : "Accept invitation"}
            </button>
            {result && <p className="staff-help">Membership #{result.id} is saved. Sign in to view your account.</p>}
          </form>
        </section>
      </main>
    </div>
  );
}

export { AcceptInvitationPage, StaffLoginPage, StaffManagementPage };
