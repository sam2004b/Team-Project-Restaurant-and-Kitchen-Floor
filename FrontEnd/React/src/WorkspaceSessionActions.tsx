import { Link } from "react-router-dom";
import { useAuth } from "./services/AuthContext";

function WorkspaceSessionActions() {
  const { signOut } = useAuth();

  return (
    <div className="workspace-session-actions">
      <Link className="secondary-button" to="/admin/staff">Account</Link>
      <button type="button" className="secondary-button" onClick={() => void signOut()}>
        Sign out
      </button>
    </div>
  );
}

export default WorkspaceSessionActions;
