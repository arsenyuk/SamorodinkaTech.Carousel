import { useCallback, useEffect, useRef, useState, type ReactNode } from "react";
import { Link } from "react-router-dom";
import {
  applyTemplate,
  clearTemplate,
  createCard,
  deleteCard,
  fetchTemplateBackgroundUrl,
  fetchTemplates,
  reorderCards,
  updateCard,
  uploadMedia,
  type CardDto,
  type CardStyleDto,
  type MediaAssetDto,
  type TemplateDto,
} from "../api";
import MediaImage from "./MediaImage";
import { StyledText, paragraphs } from "./StyledText";

// Must match CardStyleRules on the backend.
const MAX_TEXT = 500;
const MAX_PARAGRAPHS = 3;
const MAX_IMAGE_BYTES = 5 * 1024 * 1024;
const ACCEPTED = "image/jpeg,image/png,image/webp";

const ALLOWED_FONTS = ["Inter", "Playfair Display"] as const;
const ALLOWED_SIZES = ["S", "M", "L"] as const;
const ALLOWED_STYLES = ["Normal", "Bold", "Italic"] as const;

interface Props {
  publicationId: string;
  cards: CardDto[];
  onChanged: (updater: (prev: CardDto[]) => CardDto[]) => void;
  defaultStyles?: CardStyleDto[] | null;
  /** Ошибки дублируются в диалог окна (AppDialog). */
  onError?: (text: string) => void;
  /** Выбранный шаблон публикации — фон карточек без загруженного файла. */
  templateId?: string | null;
  /** Смена шаблона из формы (единый сценарий «фон карточки»). */
  onTemplateApplied?: (templateId: string | null) => void;
  /** Кнопки справа от заголовка «Карточки» (чистое превью, очистка). */
  headerActions?: ReactNode;
  /** Счётчик: растёт после очистки карточек — раскрываем выбор шаблона. */
  openTemplatePanelSeq?: number;
}

type BusyAction = "add" | `delete:${string}` | `edit:${string}` | `move:${string}` | null;

function emptyStyle(): CardStyleDto {
  return { fontName: "Inter", color: "#111111", fontSize: "M", fontStyle: "Normal" };
}

function stylesForText(text: string, defaults?: CardStyleDto[] | null): CardStyleDto[] | null {
  const count = paragraphs(text).length;
  if (count === 0) return null;
  return Array.from({ length: Math.min(count, MAX_PARAGRAPHS) }, (_, i) => defaults?.[i] ?? emptyStyle());
}

export default function CardEditor({ publicationId, cards, onChanged, defaultStyles, onError, templateId, onTemplateApplied, headerActions, openTemplatePanelSeq }: Props) {
  const [text, setText] = useState("");
  const [file, setFile] = useState<File | null>(null);
  const [previewUrl, setPreviewUrl] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);

  // Статусы «Карточка добавлена/удалена/обновлена» исчезают сами через 5 с
  useEffect(() => {
    if (!message) return;
    const t = setTimeout(() => setMessage(null), 5000);
    return () => clearTimeout(t);
  }, [message]);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState<BusyAction>(null);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editText, setEditText] = useState("");
  const [editStyles, setEditStyles] = useState<CardStyleDto[]>([]);
  const [editFile, setEditFile] = useState<File | null>(null);
  const [editRemoveImage, setEditRemoveImage] = useState(false);
  const [addFormOpen, setAddFormOpen] = useState(true);
  // Единый сценарий «фон карточки»: строка с прямыми кнопками → панель (1 клик) → возврат в строку
  const [bgPanel, setBgPanel] = useState<"chosen" | "template" | "file">("chosen");
  const [templates, setTemplates] = useState<TemplateDto[]>([]);
  const [templatePreviews, setTemplatePreviews] = useState<Record<string, string>>({});
  const [templateBusy, setTemplateBusy] = useState(false);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const editFileRef = useRef<HTMLInputElement>(null);

  function fail(message: string) {
    setError(message);
    onError?.(message);
  }

  const sorted = [...cards].sort((a, b) => a.order - b.order);
  const textTooLong = text.length > MAX_TEXT;
  const paragraphCount = paragraphs(text).length;
  const tooManyParagraphs = paragraphCount > MAX_PARAGRAPHS;
  const canAdd = (text.trim().length > 0 || file !== null) && !textTooLong && !tooManyParagraphs && busy === null;
  const showForm = addFormOpen || sorted.length === 0;

  const clearFile = useCallback(() => {
    setFile(null);
    if (previewUrl) URL.revokeObjectURL(previewUrl);
    setPreviewUrl(null);
    if (fileInputRef.current) fileInputRef.current.value = "";
  }, [previewUrl]);

  const resetForm = useCallback(() => {
    setText("");
    clearFile();
  }, [clearFile]);

  // После очистки карточек сразу раскрываем выбор шаблона (сценарий «начать заново»)
  useEffect(() => {
    if (openTemplatePanelSeq && openTemplatePanelSeq > 0) {
      setBgPanel("template");
    }
  }, [openTemplatePanelSeq]);

  // Список шаблонов для блока «Фон карточки»
  useEffect(() => {
    fetchTemplates()
      .then((list) => {
        const sorted = [...list].sort((a, b) => {
          if (a.tenantId === null && b.tenantId !== null) return -1;
          if (a.tenantId !== null && b.tenantId === null) return 1;
          return 0;
        });
        setTemplates(sorted);
      })
      .catch((e: Error) => fail(e.message));
  }, []);

  useEffect(() => {
    let cancelled = false;
    const urls: string[] = [];
    async function loadPreviews() {
      for (const t of templates) {
        try {
          const url = await fetchTemplateBackgroundUrl(t.id);
          urls.push(url);
          if (!cancelled) setTemplatePreviews((prev) => ({ ...prev, [t.id]: url }));
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

  async function onSelectTemplate(templateIdToApply: string) {
    setTemplateBusy(true);
    setError(null);
    try {
      await applyTemplate(publicationId, templateIdToApply);
      onTemplateApplied?.(templateIdToApply);
      clearFile(); // шаблон и файл — альтернативы: выбрал шаблон → файл снят
      setBgPanel("chosen"); // выбрали шаблон → панель скрывается в строку
    } catch (e) {
      console.error("[cards] apply template failed", e);
      fail(e instanceof Error ? e.message : "Не удалось применить шаблон.");
    } finally {
      setTemplateBusy(false);
    }
  }

  async function onClearTemplate() {
    setTemplateBusy(true);
    setError(null);
    try {
      await clearTemplate(publicationId);
      onTemplateApplied?.(null);
      setBgPanel("chosen");
    } catch (e) {
      console.error("[cards] clear template failed", e);
      fail(e instanceof Error ? e.message : "Не удалось снять шаблон.");
    } finally {
      setTemplateBusy(false);
    }
  }

  function resetEditFile() {
    setEditFile(null);
    setEditRemoveImage(false);
    if (editFileRef.current) editFileRef.current.value = "";
  }

  function onEditFileChange(e: React.ChangeEvent<HTMLInputElement>) {
    const next = e.target.files?.[0] ?? null;
    resetEditFile();
    setError(null);
    if (!next) return;
    if (next.size > MAX_IMAGE_BYTES) {
      fail(`Изображение больше ${Math.round(MAX_IMAGE_BYTES / 1024 / 1024)} МБ.`);
      return;
    }
    setEditFile(next);
  }

  function onFileChange(e: React.ChangeEvent<HTMLInputElement>) {
    const next = e.target.files?.[0] ?? null;
    if (previewUrl) URL.revokeObjectURL(previewUrl);
    setPreviewUrl(null);
    setFile(next);
    setError(null);
    if (next && next.size > MAX_IMAGE_BYTES) {
      fail(`Изображение больше ${Math.round(MAX_IMAGE_BYTES / 1024 / 1024)} МБ.`);
      setFile(null);
      e.target.value = "";
      return;
    }
    if (next) {
      setPreviewUrl(URL.createObjectURL(next));
      setBgPanel("chosen"); // файл выбран → панель скрывается в строку
    }
  }

  async function onAdd() {
    if (!canAdd) return;
    setBusy("add");
    setError(null);
    setMessage(null);
    try {
      let mediaAssetId: MediaAssetDto | null = null;
      if (file) {
        mediaAssetId = await uploadMedia(file);
      }
      const created = await createCard(
        publicationId,
        text.trim() || null,
        mediaAssetId?.id ?? null,
        stylesForText(text, defaultStyles),
      );
      onChanged((prev) => [...prev, created]);
      resetForm(); // форма остаётся открытой — добавление всегда одним нажатием
      setMessage("Карточка добавлена.");
    } catch (e) {
      console.error("[cards] add failed", e);
      fail(e instanceof Error ? e.message : "Не удалось добавить карточку.");
    } finally {
      setBusy(null);
    }
  }

  async function onDelete(cardId: string) {
    setBusy(`delete:${cardId}`);
    setError(null);
    setMessage(null);
    try {
      await deleteCard(publicationId, cardId);
      const wasLast = cards.length === 1;
      onChanged((prev) => prev.filter((c) => c.id !== cardId));
      if (wasLast) {
        // Последняя карточка удалена — как после «Очистить»: сразу выбор шаблона
        setBgPanel("template");
      }
      setMessage("Карточка удалена.");
    } catch (e) {
      console.error("[cards] delete failed", e);
      fail(e instanceof Error ? e.message : "Не удалось удалить карточку.");
    } finally {
      setBusy(null);
    }
  }

  function startEdit(card: CardDto) {
    setEditingId(card.id);
    setEditText(card.text ?? "");
    setEditStyles(card.styles ? [...card.styles] : stylesForText(card.text ?? "") ?? []);
    resetEditFile();
    setAddFormOpen(false); // при правке карточки форма добавления не мешает
    setError(null);
  }

  function cancelEdit() {
    setEditingId(null);
    setEditText("");
    setEditStyles([]);
    resetEditFile();
  }

  function updateEditStyle(index: number, patch: Partial<CardStyleDto>) {
    setEditStyles((prev) => {
      const next = [...prev];
      while (next.length <= index) next.push(emptyStyle());
      next[index] = { ...next[index], ...patch };
      return next;
    });
  }

  async function onSaveEdit(cardId: string) {
    setBusy(`edit:${cardId}`);
    setError(null);
    setMessage(null);
    try {
      if (editText.length > MAX_TEXT) {
        fail(`Текст не длиннее ${MAX_TEXT} символов.`);
        return;
      }
      const paraCount = paragraphs(editText).length;
      const styles = paraCount === 0
        ? null
        : editStyles.slice(0, Math.min(paraCount, MAX_PARAGRAPHS));
      let mediaAssetId: string | null = null;
      if (editFile) {
        const uploaded = await uploadMedia(editFile);
        mediaAssetId = uploaded.id;
      }
      const updated = await updateCard(publicationId, cardId, {
        text: editText.trim() || null,
        styles,
        ...(mediaAssetId ? { mediaAssetId } : {}),
        ...(editRemoveImage ? { removeImage: true } : {}),
      });
      onChanged((prev) => prev.map((c) => (c.id === cardId ? updated : c)));
      cancelEdit();
      setMessage("Карточка обновлена.");
    } catch (e) {
      console.error("[cards] update failed", e);
      fail(e instanceof Error ? e.message : "Не удалось обновить карточку.");
    } finally {
      setBusy(null);
    }
  }

  async function onMove(cardId: string, direction: -1 | 1) {
    const index = sorted.findIndex((c) => c.id === cardId);
    const target = index + direction;
    if (index < 0 || target < 0 || target >= sorted.length) return;

    const next = [...sorted];
    [next[index], next[target]] = [next[target], next[index]];
    const cardIds = next.map((c) => c.id);

    setBusy(`move:${cardId}`);
    setError(null);
    setMessage(null);
    try {
      const updatedPub = await reorderCards(publicationId, cardIds);
      const byId = new Map(updatedPub.cards.map((c) => [c.id, c]));
      onChanged((prev) => prev.map((c) => byId.get(c.id) ?? c));
    } catch (e) {
      console.error("[cards] reorder failed", e);
      fail(e instanceof Error ? e.message : "Не удалось изменить порядок.");
    } finally {
      setBusy(null);
    }
  }

  const editParagraphs = paragraphs(editText);
  const templateName = templates.find((t) => t.id === templateId)?.name ?? null;

  return (
    <section className="card-editor" aria-label="Управление карточками">
      <div className="card-editor-head">
        <h3>Карточки</h3>
        {headerActions}
      </div>

      {showForm ? (
        <div className="card-add">
          <textarea
            className="card-text-input"
            placeholder="Текст карточки. До 3 абзацев через Enter."
            value={text}
            maxLength={MAX_TEXT + 1}
            rows={3}
            onChange={(e) => {
              setText(e.target.value);
              setError(null);
            }}
          />
          <div className="card-add-meta">
            <span className={`hint${textTooLong || tooManyParagraphs ? " error" : ""}`}>
              {text.length}/{MAX_TEXT}
              {tooManyParagraphs ? ` · абзацев: ${paragraphCount}/${MAX_PARAGRAPHS}` : ""}
            </span>
          </div>

          <div className="bg-source" aria-label="Фон карточки">
            <span className="bg-source-label">Фон карточки:</span>

            {bgPanel === "template" ? (
              <div className="bg-source-panel">
                <ul className="template-grid">
                  {templates.map((t) => (
                    <li key={t.id} className={templateId === t.id ? "template-item selected" : "template-item"}>
                      <button
                        type="button"
                        className="template-btn"
                        disabled={templateBusy}
                        aria-pressed={templateId === t.id}
                        onClick={() => void onSelectTemplate(t.id)}
                      >
                        {templatePreviews[t.id] ? (
                          <img src={templatePreviews[t.id]} alt={t.name} className="template-thumb" />
                        ) : (
                          <div className="template-thumb placeholder" />
                        )}
                        <strong>{t.name}</strong>
                        <span className="badge">{t.tenantId === null ? "Системный" : "Мой"}</span>
                      </button>
                    </li>
                  ))}
                </ul>
                <div className="bg-source-actions">
                  {templateId ? (
                    <button type="button" className="linkish" disabled={templateBusy} onClick={() => void onClearTemplate()}>
                      Без шаблона
                    </button>
                  ) : null}
                  <button type="button" className="linkish" disabled={templateBusy} onClick={() => setBgPanel("chosen")}>
                    Назад
                  </button>
                  <span className="hint">
                    <Link to="/templates">Создать шаблон</Link>
                  </span>
                </div>
              </div>
            ) : bgPanel === "file" ? (
              <div className="bg-source-panel">
                <label className="file-label">
                  <span className="file-label-text">{file ? file.name : "Выберите файл"}</span>
                  <input
                    ref={fileInputRef}
                    type="file"
                    accept={ACCEPTED}
                    onChange={onFileChange}
                    aria-label="Изображение карточки"
                  />
                </label>
                {previewUrl ? <img className="card-file-preview" src={previewUrl} alt="Предпросмотр файла" /> : null}
                <div className="bg-source-actions">
                  <button type="button" className="linkish" disabled={busy !== null} onClick={() => setBgPanel("chosen")}>
                    Назад
                  </button>
                </div>
              </div>
            ) : (
              <span className="bg-source-chosen">
                <span>{file ? `файл: ${file.name}` : (templateName ?? "не задан")}</span>
                <button
                  type="button"
                  className="linkish"
                  disabled={busy !== null || templateBusy}
                  onClick={() => setBgPanel("template")}
                >
                  Из шаблона
                </button>
                <button
                  type="button"
                  className="linkish"
                  disabled={busy !== null}
                  onClick={() => setBgPanel("file")}
                >
                  {file ? "Другой файл" : "Загрузить файл"}
                </button>
                {file ? (
                  <button
                    type="button"
                    className="linkish"
                    disabled={busy !== null}
                    onClick={() => clearFile()}
                  >
                    Убрать файл
                  </button>
                ) : null}
              </span>
            )}
          </div>

          <div className="create-row">
            <button type="button" disabled={!canAdd} onClick={() => void onAdd()}>
              {busy === "add" ? "Добавление…" : "Добавить карточку"}
            </button>
            {file || text ? (
              <button type="button" className="linkish" onClick={resetForm} disabled={busy !== null}>
                Очистить
              </button>
            ) : null}
            {sorted.length > 0 ? (
              <button
                type="button"
                className="linkish"
                disabled={busy !== null}
                onClick={() => setAddFormOpen(false)}
              >
                Свернуть
              </button>
            ) : null}
          </div>
        </div>
      ) : (
        <div className="create-row">
          <button
            type="button"
            disabled={busy !== null}
            onClick={() => {
              setAddFormOpen(true);
              resetForm();
            }}
          >
            + Добавить карточку
          </button>
        </div>
      )}

      {error ? <p className="error">{error}</p> : null}
      {message ? <p className="status">{message}</p> : null}

      {sorted.length === 0 ? null : (
        <ul className="card-manage-list">
          {sorted.map((card, index) => (
            <li key={card.id} className="card-manage-item">
              <div className="card-manage-head">
                <strong>#{index + 1}</strong>
                <span className="hint">
                  {card.mediaAssetId ? "изображение" : "текст"}
                  {card.text && card.mediaAssetId ? " + текст" : ""}
                  {card.styles?.length ? ` · стилей: ${card.styles.length}` : ""}
                </span>
              </div>

              {editingId === card.id ? (
                <>
                  <textarea
                    className="card-text-input"
                    value={editText}
                    maxLength={MAX_TEXT + 1}
                    rows={3}
                    onChange={(e) => {
                      setEditText(e.target.value);
                      const count = paragraphs(e.target.value).length;
                      setEditStyles((prev) => {
                        if (count === 0) return [];
                        const next = prev.slice(0, Math.min(count, MAX_PARAGRAPHS));
                        while (next.length < Math.min(count, MAX_PARAGRAPHS)) next.push(emptyStyle());
                        return next;
                      });
                    }}
                  />
                  <div className="edit-image-row">
                    <span className="hint">Изображение:</span>
                    {editRemoveImage ? (
                      <>
                        <span className="hint">будет убрано — останется фон шаблона</span>
                        <button
                          type="button"
                          className="linkish"
                          disabled={busy !== null}
                          onClick={() => setEditRemoveImage(false)}
                        >
                          Отменить
                        </button>
                      </>
                    ) : editFile ? (
                      <>
                        <span className="hint">{editFile.name}</span>
                        <button type="button" className="linkish" disabled={busy !== null} onClick={resetEditFile}>
                          ✕ убрать выбор
                        </button>
                      </>
                    ) : card.mediaAssetId ? (
                      <>
                        <span className="edit-img-preview">
                          <MediaImage mediaAssetId={card.mediaAssetId} alt="Изображение карточки" />
                        </span>
                        <button
                          type="button"
                          className="linkish"
                          disabled={busy !== null}
                          onClick={() => editFileRef.current?.click()}
                        >
                          ⬆ Заменить
                        </button>
                        <button
                          type="button"
                          className="linkish"
                          disabled={busy !== null}
                          onClick={() => setEditRemoveImage(true)}
                        >
                          ✕ Убрать
                        </button>
                      </>
                    ) : (
                      <>
                        <span className="hint">не задано — фон шаблона</span>
                        <button
                          type="button"
                          className="linkish"
                          disabled={busy !== null}
                          onClick={() => editFileRef.current?.click()}
                        >
                          ⬆ Загрузить
                        </button>
                      </>
                    )}
                    <input
                      ref={editFileRef}
                      type="file"
                      accept={ACCEPTED}
                      hidden
                      aria-label="Заменить изображение карточки"
                      onChange={onEditFileChange}
                    />
                  </div>
                  {editParagraphs.slice(0, MAX_PARAGRAPHS).map((_, si) => (
                    <div key={si} className="style-row">
                      <span className="hint">Абзац {si + 1}</span>
                      <select
                        aria-label={`Шрифт абзаца ${si + 1}`}
                        value={editStyles[si]?.fontName ?? "Inter"}
                        onChange={(e) => updateEditStyle(si, { fontName: e.target.value })}
                      >
                        {ALLOWED_FONTS.map((f) => (
                          <option key={f} value={f}>{f}</option>
                        ))}
                      </select>
                      <select
                        aria-label={`Размер абзаца ${si + 1}`}
                        value={editStyles[si]?.fontSize ?? "M"}
                        onChange={(e) => updateEditStyle(si, { fontSize: e.target.value })}
                      >
                        {ALLOWED_SIZES.map((s) => (
                          <option key={s} value={s}>{s}</option>
                        ))}
                      </select>
                      <select
                        aria-label={`Стиль абзаца ${si + 1}`}
                        value={editStyles[si]?.fontStyle ?? "Normal"}
                        onChange={(e) => updateEditStyle(si, { fontStyle: e.target.value })}
                      >
                        {ALLOWED_STYLES.map((s) => (
                          <option key={s} value={s}>{s}</option>
                        ))}
                      </select>
                      <input
                        type="color"
                        aria-label={`Цвет абзаца ${si + 1}`}
                        value={editStyles[si]?.color ?? "#111111"}
                        onChange={(e) => updateEditStyle(si, { color: e.target.value })}
                      />
                    </div>
                  ))}
                  <div className="pub-actions">
                    <button
                      type="button"
                      disabled={busy === `edit:${card.id}` || editText.length > MAX_TEXT}
                      onClick={() => void onSaveEdit(card.id)}
                    >
                      {busy === `edit:${card.id}` ? "Сохранение…" : "Сохранить"}
                    </button>
                    <button type="button" className="linkish" onClick={cancelEdit} disabled={busy !== null}>
                      Отмена
                    </button>
                  </div>
                </>
              ) : (
                <>
                  {card.text ? (
                    <StyledText
                      text={card.text}
                      styles={card.styles ?? defaultStyles}
                      className="card-manage-text styled"
                    />
                  ) : null}
                  <div className="pub-actions">
                    <button
                      type="button"
                      className="linkish"
                      disabled={busy !== null || index === 0}
                      onClick={() => void onMove(card.id, -1)}
                      aria-label={`Переместить карточку ${index + 1} вверх`}
                    >
                      ↑
                    </button>
                    <button
                      type="button"
                      className="linkish"
                      disabled={busy !== null || index === sorted.length - 1}
                      onClick={() => void onMove(card.id, 1)}
                      aria-label={`Переместить карточку ${index + 1} вниз`}
                    >
                      ↓
                    </button>
                    <button
                      type="button"
                      className="linkish"
                      disabled={busy !== null}
                      onClick={() => startEdit(card)}
                    >
                      Править
                    </button>
                    <button
                      type="button"
                      className="linkish danger"
                      disabled={busy !== null}
                      onClick={() => void onDelete(card.id)}
                    >
                      {busy === `delete:${card.id}` ? "Удаление…" : "Удалить"}
                    </button>
                  </div>
                </>
              )}
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
