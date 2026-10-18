import { useCallback, useEffect, useMemo, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import {
  deleteTemplate,
  fetchTemplateBackgroundUrl,
  fetchTemplates,
  type TemplateDto,
} from "../api";

type Filter = "all" | "system" | "mine";

export default function TemplatesPage() {
  const [templates, setTemplates] = useState<TemplateDto[]>([]);
  const [previews, setPreviews] = useState<Record<string, string>>({});
  const [filter, setFilter] = useState<Filter>("all");
  const [error, setError] = useState<string | null>(null);
  const [busyId, setBusyId] = useState<string | null>(null);
  const navigate = useNavigate();

  const load = useCallback(async () => {
    try {
      const list = await fetchTemplates();
      setTemplates(list);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Не удалось загрузить шаблоны.");
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  useEffect(() => {
    let cancelled = false;
    const urls: string[] = [];

    async function loadPreviews() {
      for (const t of templates) {
        try {
          const url = await fetchTemplateBackgroundUrl(t.id);
          urls.push(url);
          if (!cancelled) setPreviews((prev) => ({ ...prev, [t.id]: url }));
        } catch {
          // preview optional
        }
      }
    }

    void loadPreviews();
    return () => {
      cancelled = true;
      urls.forEach((u) => URL.revokeObjectURL(u));
    };
  }, [templates]);

  const visible = useMemo(() => {
    const sorted = [...templates].sort((a, b) => {
      if (a.tenantId === null && b.tenantId !== null) return -1;
      if (a.tenantId !== null && b.tenantId === null) return 1;
      return a.name.localeCompare(b.name, "ru");
    });
    if (filter === "system") return sorted.filter((t) => t.tenantId === null);
    if (filter === "mine") return sorted.filter((t) => t.isEditable);
    return sorted;
  }, [templates, filter]);

  async function onDelete(t: TemplateDto) {
    if (!window.confirm(`Удалить шаблон «${t.name}»?`)) return;
    setBusyId(t.id);
    setError(null);
    try {
      await deleteTemplate(t.id);
      setTemplates((prev) => prev.filter((x) => x.id !== t.id));
    } catch (e) {
      setError(e instanceof Error ? e.message : "Не удалось удалить шаблон.");
    } finally {
      setBusyId(null);
    }
  }

  return (
    <main className="page">
      {/* Заголовок «Шаблоны» уже в хедере (текущий режим) — дубль убран */}
      <div className="page-head">
        <Link to="/templates/new" className="linkish">Создать</Link>
      </div>

      <div className="templates-filter" role="tablist" aria-label="Фильтр шаблонов">
        {([
          ["all", "Все"],
          ["system", "Системные"],
          ["mine", "Мои"],
        ] as const).map(([key, label]) => (
          <button
            key={key}
            type="button"
            role="tab"
            aria-selected={filter === key}
            className={filter === key ? "tab active" : "tab"}
            onClick={() => setFilter(key)}
          >
            {label}
          </button>
        ))}
      </div>

      {error ? <p className="error">{error}</p> : null}

      <ul className="template-grid templates-list">
        {visible.map((t) => (
          <li key={t.id} className="template-item">
            {previews[t.id] ? (
              <img src={previews[t.id]} alt={t.name} className="template-thumb" />
            ) : (
              <div className="template-thumb placeholder" />
            )}
            <div className="template-meta">
              <strong>{t.name}</strong>
              <span className="badge">
                {t.tenantId === null ? "Системный" : "Мой"}
              </span>
              {t.description ? <span className="hint">{t.description}</span> : null}
              {t.isEditable ? (
                <div className="template-actions">
                  <button
                    type="button"
                    className="linkish"
                    onClick={() => navigate(`/templates/${t.id}/edit`)}
                  >
                    Изменить
                  </button>
                  <button
                    type="button"
                    className="linkish danger"
                    disabled={busyId === t.id}
                    onClick={() => void onDelete(t)}
                  >
                    Удалить
                  </button>
                </div>
              ) : null}
            </div>
          </li>
        ))}
      </ul>

      {visible.length === 0 && !error ? (
        <p className="hint">Нет шаблонов для этого фильтра.</p>
      ) : null}
    </main>
  );
}
