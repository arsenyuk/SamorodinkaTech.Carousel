/**
 * Цветовая тема Mini App: палитра из Telegram themeParams (внутри Telegram)
 * или системной схемы браузера (вне Telegram). Пишет CSS-переменные на :root.
 */

interface TelegramThemeParams {
  bg_color?: string;
  secondary_bg_color?: string;
  section_bg_color?: string;
  section_separator_color?: string;
  text_color?: string;
  hint_color?: string;
  link_color?: string;
  button_color?: string;
  button_text_color?: string;
}

interface TelegramWebAppLike {
  colorScheme?: "light" | "dark";
  themeParams?: TelegramThemeParams;
  onEvent?: (eventType: string, callback: () => void) => void;
  setBackgroundColor?: (color: string) => void;
  setHeaderColor?: (color: string) => void;
}

type Palette = Record<string, string>;

/** Текущая светлая палитра приложения (дефолт, как было до темы). */
const LIGHT: Palette = {
  "--bg": "#f6f7f9",
  "--surface": "#ffffff",
  "--surface-soft": "#f3f4f6",
  "--text": "#111111",
  "--text-soft": "#333333",
  "--hint": "#555555",
  "--muted": "#9ca3af",
  "--border": "#e5e7eb",
  "--border-strong": "#d1d5db",
  "--accent": "#2563eb",
  "--on-accent": "#ffffff",
  "--danger": "#b91c1c",
  "--success": "#15803d",
  "--badge-bg": "#eef2ff",
  "--badge-text": "#3730a3",
};

/** Тёмная палитра (близко к ночной теме Telegram). */
const DARK: Palette = {
  "--bg": "#17212b",
  "--surface": "#232e3c",
  "--surface-soft": "#2b3948",
  "--text": "#ffffff",
  "--text-soft": "#dde3e9",
  "--hint": "#8a96a3",
  "--muted": "#6c7a89",
  "--border": "#2f3a47",
  "--border-strong": "#3a4653",
  "--accent": "#5288c1",
  "--on-accent": "#ffffff",
  "--danger": "#e05454",
  "--success": "#4caf50",
  "--badge-bg": "#2b3a4c",
  "--badge-text": "#9ec1e3",
};

function applyPalette(palette: Palette): void {
  const root = document.documentElement;
  for (const [name, value] of Object.entries(palette)) {
    root.style.setProperty(name, value);
  }
}

export function applyTelegramTheme(): void {
  const w = window as unknown as { Telegram?: { WebApp?: TelegramWebAppLike } };
  const webApp = w.Telegram?.WebApp;

  if (!webApp) {
    // Вне Telegram — системная тема браузера (удобно для dev-предпросмотра).
    const prefersDark = window.matchMedia?.("(prefers-color-scheme: dark)").matches ?? false;
    applyPalette(prefersDark ? DARK : LIGHT);
    return;
  }

  const base = webApp.colorScheme === "dark" ? DARK : LIGHT;
  const tp = webApp.themeParams ?? {};
  applyPalette({
    ...base,
    ...(tp.bg_color ? { "--bg": tp.bg_color } : {}),
    ...(tp.secondary_bg_color
      ? { "--surface": tp.secondary_bg_color }
      : tp.section_bg_color
        ? { "--surface": tp.section_bg_color }
        : {}),
    ...(tp.text_color ? { "--text": tp.text_color, "--text-soft": tp.text_color } : {}),
    ...(tp.hint_color ? { "--hint": tp.hint_color, "--muted": tp.hint_color } : {}),
    ...(tp.section_separator_color ? { "--border": tp.section_separator_color } : {}),
    ...(tp.button_color ? { "--accent": tp.button_color } : tp.link_color ? { "--accent": tp.link_color } : {}),
    ...(tp.button_text_color ? { "--on-accent": tp.button_text_color } : {}),
  });

  try {
    webApp.setBackgroundColor?.(tp.bg_color ?? base["--bg"]);
    webApp.setHeaderColor?.(tp.secondary_bg_color ?? tp.bg_color ?? base["--bg"]);
  } catch {
    // Старые клиенты могут не поддерживать set*Color — тема применится через CSS.
  }
}