import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
} from "react";
import type { ReactNode } from "react";
import {
  authorizationDeniedEvent,
  clearStoredAccessToken,
  getCurrentAccount,
  getStoredAccessToken,
  login,
  logout,
  sessionExpiredEvent,
  storeAccessToken,
} from "./api";
import type { AccountResponse } from "./api";

type AuthStatus = "loading" | "authenticated" | "anonymous" | "error";
type LoginCredentials = { username: string; password: string };

type AuthContextValue = {
  account: AccountResponse | null;
  status: AuthStatus;
  sessionMessage: string | null;
  signIn: (credentials: LoginCredentials) => Promise<AccountResponse>;
  signOut: () => Promise<string | null>;
  refreshAccount: () => Promise<AccountResponse | null>;
};

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [account, setAccount] = useState<AccountResponse | null>(null);
  const [status, setStatus] = useState<AuthStatus>("loading");
  const [sessionMessage, setSessionMessage] = useState<string | null>(null);
  const requestVersion = useRef(0);
  const accountRef = useRef(account);
  accountRef.current = account;

  const refreshAccount = useCallback(async () => {
    const version = ++requestVersion.current;
    let token: string | null;
    try {
      token = getStoredAccessToken();
    } catch (error) {
      setStatus("error");
      throw error;
    }

    if (!token) {
      setAccount(null);
      setStatus("anonymous");
      return null;
    }

    try {
      const currentAccount = await getCurrentAccount();
      if (version === requestVersion.current) {
        setAccount(currentAccount);
        setStatus("authenticated");
        setSessionMessage(null);
      }
      return currentAccount;
    } catch (error) {
      if (version === requestVersion.current) {
        if (!getStoredAccessToken()) {
          setAccount(null);
          setStatus("anonymous");
        } else {
          setStatus(accountRef.current ? "authenticated" : "error");
          if (!accountRef.current) {
            setSessionMessage(error instanceof Error
              ? `Could not verify the saved session: ${error.message}`
              : "Could not verify the saved session.");
          }
        }
      }
      throw error;
    }
  }, []);

  useEffect(() => {
    let active = true;
    let token: string | null;
    let storageAvailable = true;
    try {
      token = getStoredAccessToken();
    } catch (error) {
      storageAvailable = false;
      setStatus("error");
      setSessionMessage(error instanceof Error
        ? `Could not read the saved session: ${error.message}`
        : "Could not read the saved session.");
      token = null;
    }

    if (storageAvailable && !token) {
      setAccount(null);
      setStatus("anonymous");
    } else if (storageAvailable && token) {
      void refreshAccount().catch(() => {
        if (active && getStoredAccessToken()) setStatus("error");
      });
    }

    const handleSessionExpired = () => {
      requestVersion.current += 1;
      setAccount(null);
      setStatus("anonymous");
      setSessionMessage("Your session expired. Sign in again to continue.");
    };
    const handleAuthorizationDenied = () => {
      void refreshAccount().catch(() => {
        if (active && !getStoredAccessToken()) {
          setAccount(null);
          setStatus("anonymous");
        }
      });
    };

    window.addEventListener(sessionExpiredEvent, handleSessionExpired);
    window.addEventListener(authorizationDeniedEvent, handleAuthorizationDenied);
    return () => {
      active = false;
      window.removeEventListener(sessionExpiredEvent, handleSessionExpired);
      window.removeEventListener(authorizationDeniedEvent, handleAuthorizationDenied);
    };
  }, [refreshAccount]);

  const signIn = useCallback(async ({ username, password }: LoginCredentials) => {
    setSessionMessage(null);
    setStatus("loading");
    setAccount(null);
    try {
      clearStoredAccessToken();
      const response = await login(username, password);
      if (!response.accessToken) {
        throw new Error("The API did not return an access token.");
      }
      storeAccessToken(response.accessToken);
      const currentAccount = await refreshAccount();
      if (!currentAccount) {
        throw new Error("The signed-in account could not be verified.");
      }
      return currentAccount;
    } catch (error) {
      try {
        clearStoredAccessToken();
      } catch {
        setSessionMessage("Sign-in failed, and the previous browser session could not be cleared.");
      }
      setAccount(null);
      setStatus("anonymous");
      throw error;
    }
  }, [refreshAccount]);

  const signOut = useCallback(async () => {
    let warning: string | null = null;
    try {
      if (getStoredAccessToken()) await logout();
    } catch (error) {
      warning = error instanceof Error
        ? `Signed out on this device, but the server session could not be revoked: ${error.message}`
        : "Signed out on this device, but the server session could not be revoked.";
    } finally {
      requestVersion.current += 1;
      try {
        clearStoredAccessToken();
      } catch (error) {
        const detail = error instanceof Error ? error.message : "session storage is unavailable.";
        warning = `${warning ? `${warning} ` : ""}The local session could not be cleared: ${detail}`;
      }
      setAccount(null);
      setStatus("anonymous");
      setSessionMessage(warning ?? "You have signed out.");
    }
    return warning;
  }, []);

  const value = useMemo<AuthContextValue>(() => ({
    account,
    status,
    sessionMessage,
    signIn,
    signOut,
    refreshAccount,
  }), [account, refreshAccount, sessionMessage, signIn, signOut, status]);

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error("useAuth must be used within AuthProvider.");
  }
  return context;
}
