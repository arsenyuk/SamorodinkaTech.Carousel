import { useEffect, useState } from "react";
import { Link, Navigate, Route, Routes, useLocation, useNavigate } from "react-router-dom";
import {
  clearCurrentPublicationId,
  clearSession,
  createPublication,
  ensureTenant,
  fetchBotUsername,
  fetchBusinessConnection,
  fetchMe,
  fetchPublication,
  fetchStarsConfig,
  getCurrentPublicationId,
  getTelegramInitData,
  getToken,
  loginWithInitData,
  setCurrentPublicationId,
  type PublicationDto,
  type UserDto,
} from "./api";
import EditorPage from "./pages/EditorPage";
import StatsPage from "./pages/StatsPage";
import TemplatesPage from "./pages/TemplatesPage";
import TemplateEditPage from "./pages/TemplateEditPage";
import { applyTelegramTheme } from "./theme";

export default function App() {
  const [user, setUser] = useState<UserDto | null>(null);
  const [authError, setAuthError] = useState<string | null>(null);
  const [booting, setBooting] = useState(true);
  const [currentPub, setCurrentPub] = useState<PublicationDto | null>(null);
  // Business connection for stories: null = ещё не загружен, false = не подключён.
  const [historyConnected, setHistoryConnected] = useState<boolean | null>(null);
  // Username бота для ссылок на карточках: null = не загружен/недоступен.
  const [botUsername, setBotUsername] = useState<string | null>(null);
  // Цена публикации в Stars (для кнопки), null = не загружена.
  const [starsPrice, setStarsPrice] = useState<number | null>(null);
  const navigate = useNavigate();
  const location = useLocation();
  const onTemplatesPage = location.pathname.startsWith("/templates");
  const onStatsPage = location.pathname.startsWith("/stats");
  // Превью — штатный режим: /edit/:id?preview=1 (возврат — ссылка «Редактор» в хедере).
  const onPreviewPage =
    location.pathname.startsWith("/edit/") &&
    new URLSearchParams(location.search).get("preview") === "1";

  // Тема Telegram — до первой отрисовки (идемпотентно, вызов на каждый рендер безопасен).
  applyTelegramTheme();

  useEffect(() => {
    const w = window as unknown as {
      Telegram?: { WebApp?: { onEvent?: (t: string, cb: () => void) => void } };
    };
    w.Telegram?.WebApp?.onEvent?.("themeChanged", applyTelegramTheme);
    window.addEventListener("telegram-theme-changed", applyTelegramTheme);
    return () => window.removeEventListener("telegram-theme-changed", applyTelegramTheme);
  }, []);

  // Inside the Telegram Mini App there is no login UI: if auth fails the app closes itself.
  function terminateIfInTelegram(): boolean {
    const w = window as unknown as { Telegram?: { WebApp?: { close?: () => void } } };
    const webApp = w.Telegram?.WebApp;
    if (!webApp?.close) return false;
    setTimeout(() => webApp.close?.(), 1200);
    return true;
  }

  useEffect(() => {
    let cancelled = false;
    async function boot() {
      try {
        const initData = getTelegramInitData();
        if (initData && !getToken()) {
          await loginWithInitData(initData);
        }

        if (getToken()) {
          try {
            await fetchMe();
            await ensureTenant();
            if (!cancelled) {
              const me = await fetchMe();
              setUser(me);

              // Статус business-connection и username бота — сразу на старте приложения.
              fetchBusinessConnection()
                .then((s) => { if (!cancelled) setHistoryConnected(s.connected); })
                .catch(() => { if (!cancelled) setHistoryConnected(null); });
              fetchBotUsername()
                .then((s) => { if (!cancelled) setBotUsername(s.username || null); })
                .catch(() => { if (!cancelled) setBotUsername(null); });
              fetchStarsConfig()
                .then((s) => { if (!cancelled) setStarsPrice(s.price); })
                .catch(() => { if (!cancelled) setStarsPrice(null); });

              // Don't steal /templates*, /stats and preview routes on boot.
              const path = window.location.pathname;
              const onPreview =
                path.startsWith("/edit/") &&
                new URLSearchParams(window.location.search).get("preview") === "1";
              const skipResolve =
                path.startsWith("/templates") || path.startsWith("/stats") || onPreview;
              if (!skipResolve) {
                const pub = await resolveCurrentPublication();
                if (cancelled) return;
                setCurrentPub(pub);
                navigate(`/edit/${pub.id}`, { replace: true });
              }
            }
          } catch (e) {
            console.error("[boot] session restore failed", e);
            clearSession();
            clearCurrentPublicationId();
            if (!cancelled) {
              const closing = terminateIfInTelegram();
              setAuthError(closing ? "Вход не выполнен — окно закроется." : "Ошибка входа.");
            }
          }
        } else if (!initData && !cancelled) {
          // Inside Telegram without initData means auth is broken — terminate.
          if (terminateIfInTelegram()) setAuthError("Вход не выполнен — окно закроется.");
        }
      } catch (e) {
        console.error("[boot] auth failed", e);
        if (!cancelled) {
          clearSession();
          const closing = terminateIfInTelegram();
          setAuthError(
            closing
              ? "Вход не выполнен — окно закроется."
              : e instanceof Error ? e.message : "Ошибка входа.",
          );
        }
      } finally {
        if (!cancelled) setBooting(false);
      }
    }
    void boot();
    return () => {
      cancelled = true;
    };
  }, [navigate]);

  if (booting) {
    return <div className="app"><main className="page">Загрузка…</main></div>;
  }

  return (
    <div className="app">
      <header className="header">
        {user ? (
          <>
            {/* Текущий режим — слева, без нажатия; ссылки — справа */}
            <span className="header-current">
              {onStatsPage
                ? "Статистика"
                : onTemplatesPage
                  ? "Шаблоны"
                  : onPreviewPage
                    ? "Превью"
                    : "Редактор"}
            </span>
            <span className="header-links">
              {onPreviewPage ? (
                /* Из превью возврат — штатная навигация тем же маршрутом без query */
                <Link to={location.pathname} className="linkish">Редактор</Link>
              ) : onTemplatesPage || onStatsPage ? (
                <Link to="/" className="linkish">Редактор</Link>
              ) : null}
              {!onTemplatesPage && !onStatsPage ? (
                <Link to="/templates" className="linkish">Шаблоны</Link>
              ) : null}
              {user.isAdmin && !onStatsPage ? (
                <Link to="/stats" className="linkish">Статистика</Link>
              ) : null}
            </span>
          </>
        ) : null}
      </header>
      {authError ? <p className="error auth-error">{authError}</p> : null}
      <Routes>
        <Route
          path="/"
          element={
            user && currentPub ? (
              <Navigate to={`/edit/${currentPub.id}`} replace />
            ) : (
              <main className="page">
                {user ? (
                  <p className="hint">Загрузка редактора…</p>
                ) : (
                  <p className="hint">
                    Вход не выполнен. Для локальной разработки откройте с{" "}
                    {"?tgInitData=<telegram id>"}.
                  </p>
                )}
              </main>
            )
          }
        />
        <Route path="/edit/:id" element={<EditorPage user={user} historyConnected={historyConnected} botUsername={botUsername} starsPrice={starsPrice} />} />
        <Route path="/templates" element={user ? <TemplatesPage /> : <Navigate to="/" replace />} />
        <Route path="/stats" element={user ? <StatsPage /> : <Navigate to="/" replace />} />
        <Route path="/templates/new" element={user ? <TemplateEditPage mode="create" /> : <Navigate to="/" replace />} />
        <Route path="/templates/:id/edit" element={user ? <TemplateEditPage mode="edit" /> : <Navigate to="/" replace />} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </div>
  );
}

/** Get-or-create the current publication for the editor. */
async function resolveCurrentPublication(): Promise<PublicationDto> {
  const savedId = getCurrentPublicationId();
  if (savedId) {
    try {
      const pub = await fetchPublication(savedId);
      setCurrentPublicationId(pub.id);
      return pub;
    } catch (e) {
      const status = (e as Error & { status?: number }).status;
      if (status !== 401 && status !== 403 && status !== 404) throw e;
      clearCurrentPublicationId();
    }
  }
  const created = await createPublication("Карусель");
  setCurrentPublicationId(created.id);
  return created;
}
