import { useEffect, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import {
  createTemplate,
  fetchTemplate,
  updateTemplate,
  uploadMedia,
  type CardStyleDto,
} from "../api";
import TemplateBackgroundFields, { type BackgroundMode } from "../components/TemplateBackgroundFields";
import TemplateStyleFields from "../components/TemplateStyleFields";

interface Props {
  mode: "create" | "edit";
}

const DEFAULT_STYLES: CardStyleDto[] = [
  { fontName: "Playfair Display", color: "#FFFFFF", fontSize: "L", fontStyle: "Normal" },
  { fontName: "Inter", color: "#F0F4FF", fontSize: "M", fontStyle: "Normal" },
  { fontName: "Inter", color: "#D9E8FF", fontSize: "S", fontStyle: "Normal" },
];

export default function TemplateEditPage({ mode }: Props) {
  const { id } = useParams();
  const navigate = useNavigate();

  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [styles, setStyles] = useState<CardStyleDto[]>(DEFAULT_STYLES);
  const [bgMode, setBgMode] = useState<BackgroundMode>("gradient");
  const [gradientFrom, setGradientFrom] = useState("#0D7377");
  const [gradientTo, setGradientTo] = useState("#6B4E9B");
  const [uploadFile, setUploadFile] = useState<File | null>(null);
  const [uploadPreviewUrl, setUploadPreviewUrl] = useState<string | null>(null);
  const [loading, setLoading] = useState(mode === "edit");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (mode !== "edit" || !id) return;
    fetchTemplate(id)
      .then((t) => {
        setName(t.name);
        setDescription(t.description ?? "");
        setStyles(t.defaultStyles ?? DEFAULT_STYLES);
      })
      .catch((e: Error) => setError(e.message))
      .finally(() => setLoading(false));
  }, [id, mode]);

  useEffect(() => {
    if (!uploadFile) {
      setUploadPreviewUrl(null);
      return;
    }
    const url = URL.createObjectURL(uploadFile);
    setUploadPreviewUrl(url);
    return () => URL.revokeObjectURL(url);
  }, [uploadFile]);

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!name.trim()) {
      setError("Укажите имя шаблона.");
      return;
    }

    setSaving(true);
    setError(null);
    try {
      let backgroundMediaAssetId: string | null = null;
      let gradientFromHex: string | null = null;
      let gradientToHex: string | null = null;

      if (bgMode === "upload") {
        if (!uploadFile && mode === "create") {
          setError("Выберите файл фона.");
          setSaving(false);
          return;
        }
        if (uploadFile) {
          const media = await uploadMedia(uploadFile);
          backgroundMediaAssetId = media.id;
        }
      } else {
        gradientFromHex = gradientFrom;
        gradientToHex = gradientTo;
      }

      if (mode === "create") {
        await createTemplate({
          name: name.trim(),
          description: description.trim() || null,
          defaultStyles: styles,
          backgroundMediaAssetId,
          gradientFromHex,
          gradientToHex,
        });
      } else if (id) {
        await updateTemplate(id, {
          name: name.trim(),
          description: description.trim() || null,
          defaultStyles: styles,
          backgroundMediaAssetId,
          gradientFromHex,
          gradientToHex,
        });
      }

      navigate("/templates");
    } catch (err) {
      console.error("[template-edit] save failed", err);
      setError(err instanceof Error ? err.message : "Не удалось сохранить шаблон.");
    } finally {
      setSaving(false);
    }
  }

  if (loading) {
    return <main className="page">Загрузка…</main>;
  }

  return (
    <main className="page">
      <div className="page-head">
        <h2>{mode === "create" ? "Новый шаблон" : "Изменить шаблон"}</h2>
      </div>

      <form className="template-edit-form" onSubmit={(e) => void onSubmit(e)}>
        <label>
          Имя *
          <input
            type="text"
            value={name}
            maxLength={200}
            required
            onChange={(e) => setName(e.target.value)}
          />
        </label>
        <label>
          Описание
          <textarea
            value={description}
            maxLength={1000}
            rows={2}
            onChange={(e) => setDescription(e.target.value)}
          />
        </label>

        <TemplateBackgroundFields
          mode={bgMode}
          gradientFrom={gradientFrom}
          gradientTo={gradientTo}
          uploadFile={uploadFile}
          uploadPreviewUrl={uploadPreviewUrl}
          onModeChange={setBgMode}
          onGradientFromChange={setGradientFrom}
          onGradientToChange={setGradientTo}
          onFileChange={setUploadFile}
        />

        <TemplateStyleFields styles={styles} onChange={setStyles} />

        {error ? <p className="error">{error}</p> : null}

        <div className="form-actions">
          <button type="submit" disabled={saving}>
            {saving ? "Сохранение…" : "Сохранить"}
          </button>
          <button type="button" className="linkish" onClick={() => navigate("/templates")}>
            Отмена
          </button>
        </div>
      </form>
    </main>
  );
}
