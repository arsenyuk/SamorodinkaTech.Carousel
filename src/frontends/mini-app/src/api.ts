const API_BASE = import.meta.env.VITE_API_BASE_URL ?? "http://localhost:5050";

export interface CardStyleDto {
  fontName: string;
  color: string;
  fontSize: string;
  fontStyle: string;
}

export interface CardDto {
  id: string;
  order: number;
  text: string | null;
  mediaAssetId: string | null;
  styles?: CardStyleDto[] | null;
}

export interface PublicationDto {
  id: string;
  tenantId: string;
  title: string;
  description: string | null;
  status: string;
  templateId: string | null;
  cards: CardDto[];
  targets: { id: string; platform: string; platformAccountId: string; status: string }[];
  noAds?: boolean;
}

export interface TemplateDto {
  id: string;
  code: string;
  name: string;
  description: string | null;
  backgroundMediaAssetId: string | null;
  defaultStyles: CardStyleDto[] | null;
  tenantId: string | null;
  isEditable: boolean;
}

export interface CreateTemplatePayload {
  name: string;
  code?: string | null;
  description?: string | null;
  defaultStyles?: CardStyleDto[] | null;
  backgroundMediaAssetId?: string | null;
  gradientFromHex?: string | null;
  gradientToHex?: string | null;
}

export type UpdateTemplatePayload = Omit<CreateTemplatePayload, "name" | "code"> & {
  name?: string | null;
};

export interface UserDto {
  id: string;
  telegramUserId: number;
  telegramUsername: string | null;
  displayName: string | null;
  isAdmin: boolean;
}

// —— Админская статистика (GET /api/admin/stats) ——
export interface AdminStatsDto {
  days: number;
  from: string;
  totals: { publications: number; starsAmount: number; starsCount: number };
  dayStats: { date: string; publications: number; starsAmount: number; starsCount: number }[];
  byTemplate: { name: string; count: number }[];
  byCardCount: { bucket: string; count: number }[];
  counters: { withBotAds: number; noAds: number; withOwnImages: number };
}

const TOKEN_KEY = "carousel_token";
const TENANT_KEY = "carousel_tenant";
const CURRENT_PUB_KEY = "carousel_current_pub";

export function getToken(): string | null {
  return localStorage.getItem(TOKEN_KEY);
}

export function setSession(token: string, tenantId?: string) {
  localStorage.setItem(TOKEN_KEY, token);
  if (tenantId) {
    localStorage.setItem(TENANT_KEY, tenantId);
  }
}

export function getTenantId(): string | null {
  return localStorage.getItem(TENANT_KEY);
}

export function clearSession() {
  localStorage.removeItem(TOKEN_KEY);
  localStorage.removeItem(TENANT_KEY);
}

export function getCurrentPublicationId(): string | null {
  return localStorage.getItem(CURRENT_PUB_KEY);
}

export function setCurrentPublicationId(id: string): void {
  localStorage.setItem(CURRENT_PUB_KEY, id);
}

export function clearCurrentPublicationId(): void {
  localStorage.removeItem(CURRENT_PUB_KEY);
}

function authHeaders(): Record<string, string> {
  const token = getToken();
  const tenant = getTenantId();
  const headers: Record<string, string> = {};
  if (token) headers.Authorization = `Bearer ${token}`;
  if (tenant) headers["X-Tenant-Id"] = tenant;
  return headers;
}

async function request(path: string, init?: RequestInit): Promise<Response> {
  const requestId = crypto.randomUUID();
  const url = `${API_BASE}${path}`;
  const method = init?.method ?? "GET";
  const headers: Record<string, string> = {
    ...((init?.headers as Record<string, string>) ?? {}),
    "X-Request-Id": requestId,
  };

  let res: Response;
  try {
    res = await fetch(url, { ...init, headers });
  } catch (e) {
    console.error("[api]", method, path, "network error", e, { requestId });
    throw e instanceof Error ? e : new Error(`Network error: ${path}`);
  }

  if (!res.ok) {
    let detail = "";
    let traceId: string | undefined;
    try {
      const body = (await res.json()) as { detail?: string; title?: string; extensions?: Record<string, unknown> };
      detail = body.detail ?? body.title ?? "";
      // ProblemDetails may put traceId in extensions or top-level
      const raw = (body as Record<string, unknown>).traceId ?? body.extensions?.traceId;
      if (typeof raw === "string") traceId = raw;
    } catch {
      // non-JSON body
    }
    console.error("[api]", method, path, res.status, detail, { requestId, traceId });
    const suffix = detail ? `: ${detail}` : "";
    const err = new Error(`${method} ${path} → ${res.status}${suffix}`);
    (err as Error & { status?: number; requestId?: string; traceId?: string }).status = res.status;
    (err as Error & { status?: number; requestId?: string; traceId?: string }).requestId = requestId;
    if (traceId) {
      (err as Error & { status?: number; requestId?: string; traceId?: string }).traceId = traceId;
    }
    throw err;
  }

  return res;
}

export async function loginWithInitData(initData: string): Promise<UserDto> {
  const res = await request("/api/auth/telegram", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ initData }),
  });
  const body = await res.json() as { token: string; user: UserDto };
  setSession(body.token);
  return body.user;
}

export async function fetchMe(): Promise<UserDto> {
  const res = await request("/api/me", { headers: authHeaders() });
  return res.json();
}

export async function fetchTenants(): Promise<{ id: string; name: string }[]> {
  const res = await request("/api/tenants", { headers: authHeaders() });
  return res.json();
}

export async function createTenant(name: string): Promise<{ id: string; name: string }> {
  const res = await request("/api/tenants", {
    method: "POST",
    headers: { "Content-Type": "application/json", ...authHeaders() },
    body: JSON.stringify({ name }),
  });
  return res.json();
}

export async function ensureTenant(): Promise<string> {
  const existing = getTenantId();
  if (existing) return existing;

  let tenants = await fetchTenants();
  if (tenants.length === 0) {
    const created = await createTenant("Мой workspace");
    setSession(getToken()!, created.id);
    return created.id;
  }

  setSession(getToken()!, tenants[0].id);
  return tenants[0].id;
}

export async function fetchPublications(): Promise<PublicationDto[]> {
  const res = await request("/api/publications", { headers: authHeaders() });
  return res.json();
}

export async function fetchPublication(id: string): Promise<PublicationDto> {
  const res = await request(`/api/publications/${id}`, { headers: authHeaders() });
  return res.json();
}

export async function createPublication(title: string, description?: string): Promise<PublicationDto> {
  const res = await request("/api/publications", {
    method: "POST",
    headers: { "Content-Type": "application/json", ...authHeaders() },
    body: JSON.stringify({ title, description }),
  });
  return res.json();
}

export async function createCard(
  publicationId: string,
  text: string | null,
  mediaAssetId?: string | null,
  styles?: CardStyleDto[] | null,
): Promise<CardDto> {
  const res = await request(`/api/publications/${publicationId}/cards`, {
    method: "POST",
    headers: { "Content-Type": "application/json", ...authHeaders() },
    body: JSON.stringify({ text: text || null, mediaAssetId: mediaAssetId ?? null, styles: styles ?? null }),
  });
  return res.json();
}

export async function updateCard(
  publicationId: string,
  cardId: string,
  patch: { text?: string | null; mediaAssetId?: string | null; order?: number; styles?: CardStyleDto[] | null; removeImage?: boolean },
): Promise<CardDto> {
  const res = await request(`/api/publications/${publicationId}/cards/${cardId}`, {
    method: "PATCH",
    headers: { "Content-Type": "application/json", ...authHeaders() },
    body: JSON.stringify(patch),
  });
  return res.json();
}

export async function deleteCard(publicationId: string, cardId: string): Promise<void> {
  await request(`/api/publications/${publicationId}/cards/${cardId}`, {
    method: "DELETE",
    headers: authHeaders(),
  });
}

export async function clearCards(publicationId: string): Promise<void> {
  await request(`/api/publications/${publicationId}/cards`, {
    method: "DELETE",
    headers: authHeaders(),
  });
}

export async function reorderCards(publicationId: string, cardIds: string[]): Promise<PublicationDto> {
  const res = await request(`/api/publications/${publicationId}/cards/reorder`, {
    method: "POST",
    headers: { "Content-Type": "application/json", ...authHeaders() },
    body: JSON.stringify({ cardIds }),
  });
  return res.json();
}

export interface MediaAssetDto {
  id: string;
  contentType: string;
  sizeBytes: number;
  width: number | null;
  height: number | null;
}

export async function uploadMedia(file: File): Promise<MediaAssetDto> {
  const form = new FormData();
  form.append("file", file);
  const res = await request("/api/media", {
    method: "POST",
    headers: authHeaders(),
    body: form,
  });
  return res.json();
}

export async function fetchMediaBlobUrl(mediaAssetId: string): Promise<string> {
  const res = await request(`/api/media/${mediaAssetId}`, { headers: authHeaders() });
  const blob = await res.blob();
  return URL.createObjectURL(blob);
}

/** Байты карточки ровно в publish-виде (фото / рендер PNG 1080×1920 + печать ссылки). */
export async function fetchStoryImage(publicationId: string, cardId: string): Promise<Blob> {
  const res = await request(`/api/publications/${publicationId}/cards/${cardId}/story-image`, {
    headers: authHeaders(),
  });
  return res.blob();
}

export async function markReady(publicationId: string): Promise<void> {
  await request(`/api/publications/${publicationId}/ready`, {
    method: "POST",
    headers: authHeaders(),
  });
}

export async function fetchTemplates(): Promise<TemplateDto[]> {
  const res = await request("/api/templates", { headers: authHeaders() });
  return res.json();
}

export async function fetchTemplate(id: string): Promise<TemplateDto> {
  const res = await request(`/api/templates/${id}`, { headers: authHeaders() });
  return res.json();
}

export async function createTemplate(payload: CreateTemplatePayload): Promise<TemplateDto> {
  const res = await request("/api/templates", {
    method: "POST",
    headers: { "Content-Type": "application/json", ...authHeaders() },
    body: JSON.stringify(payload),
  });
  return res.json();
}

export async function updateTemplate(id: string, payload: UpdateTemplatePayload): Promise<TemplateDto> {
  const res = await request(`/api/templates/${id}`, {
    method: "PUT",
    headers: { "Content-Type": "application/json", ...authHeaders() },
    body: JSON.stringify(payload),
  });
  return res.json();
}

export async function deleteTemplate(id: string): Promise<void> {
  await request(`/api/templates/${id}`, {
    method: "DELETE",
    headers: authHeaders(),
  });
}

export async function fetchTemplateBackgroundUrl(templateId: string): Promise<string> {
  const res = await request(`/api/templates/${templateId}/background`, { headers: authHeaders() });
  const blob = await res.blob();
  return URL.createObjectURL(blob);
}

export async function applyTemplate(publicationId: string, templateId: string): Promise<PublicationDto> {
  const res = await request(`/api/publications/${publicationId}`, {
    method: "PATCH",
    headers: { "Content-Type": "application/json", ...authHeaders() },
    body: JSON.stringify({ templateId }),
  });
  return res.json();
}

export async function clearTemplate(publicationId: string): Promise<PublicationDto> {
  const res = await request(`/api/publications/${publicationId}`, {
    method: "PATCH",
    headers: { "Content-Type": "application/json", ...authHeaders() },
    body: JSON.stringify({ clearTemplate: true }),
  });
  return res.json();
}

export async function fetchBusinessConnection(): Promise<{ connected: boolean }> {
  const res = await request("/api/telegram/business-connection", { headers: authHeaders() });
  return res.json();
}

export async function fetchBotUsername(): Promise<{ username: string }> {
  const res = await request("/api/telegram/bot-username", { headers: authHeaders() });
  return res.json();
}

export async function fetchStarsConfig(): Promise<{ price: number }> {
  const res = await request("/api/billing/stars-config", { headers: authHeaders() });
  return res.json();
}

export async function fetchAdminStats(days: number): Promise<AdminStatsDto> {
  const res = await request(`/api/admin/stats?days=${days}`, { headers: authHeaders() });
  return res.json();
}

export async function publishToMe(
  id: string,
  noAds = false,
): Promise<{ status: string; invoiceUrl?: string }> {
  const res = await request(`/api/publications/${id}/publish/me`, {
    method: "POST",
    headers: { "Content-Type": "application/json", ...authHeaders() },
    body: JSON.stringify({ noAds }),
  });
  return res.json();
}

export function getTelegramInitData(): string {
  const w = window as unknown as { Telegram?: { WebApp?: { initData?: string; initDataUnsafe?: unknown } } };
  const fromWebApp = w.Telegram?.WebApp?.initData;
  if (fromWebApp) return fromWebApp;
  // Dev fallback: query ?tgInitData= or localStorage
  const url = new URL(window.location.href);
  const q = url.searchParams.get("tgInitData");
  if (q) return q;
  return localStorage.getItem("dev_init_data") ?? "";
}

export { API_BASE };
