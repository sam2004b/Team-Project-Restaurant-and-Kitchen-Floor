import { useEffect, useState } from "react";
import { Link, Navigate, Route, Routes } from "react-router-dom";
import { getApiHealth } from "./services/api";

const routes = [
  { path: "/", label: "Home" },
  { path: "/orders", label: "Orders" },
  { path: "/kitchen", label: "Kitchen" },
  { path: "/menu", label: "Menu" },
  { path: "/reservations", label: "Reservations" },
];

function HomePage() {
  const [apiMessage, setApiMessage] = useState("Checking API...");

  useEffect(() => {
    let active = true;

    getApiHealth()
      .then(({ status }) => {
        if (active) setApiMessage(`API status: ${status}`);
      })
      .catch((error: unknown) => {
        if (active) {
          setApiMessage(error instanceof Error ? error.message : "API request failed.");
        }
      });

    return () => {
      active = false;
    };
  }, []);

  return (
    <section>
      <h1>Home</h1>
      <p>Frontend starter page.</p>
      <p role="status">{apiMessage}</p>
    </section>
  );
}

function PlaceholderPage({ title }: { title: string }) {
  return (
    <section>
      <h1>{title}</h1>
      <p>This page is a placeholder.</p>
    </section>
  );
}

export default function App() {
  return (
    <>
      <header>
        <nav aria-label="Main navigation">
          {routes.map(({ path, label }) => (
            <Link key={path} to={path}>{label}</Link>
          ))}
        </nav>
      </header>
      <main>
        <Routes>
          <Route path="/" element={<HomePage />} />
          {routes.slice(1).map(({ path, label }) => (
            <Route key={path} path={path} element={<PlaceholderPage title={label} />} />
          ))}
          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </main>
    </>
  );
}
