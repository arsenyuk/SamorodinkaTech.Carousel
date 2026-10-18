import { useEffect, useState } from "react";

export type BackgroundMode = "gradient" | "upload";

interface Props {
  mode: BackgroundMode;
  gradientFrom: string;
  gradientTo: string;
  uploadFile: File | null;
  uploadPreviewUrl: string | null;
  onModeChange: (mode: BackgroundMode) => void;
  onGradientFromChange: (hex: string) => void;
  onGradientToChange: (hex: string) => void;
  onFileChange: (file: File | null) => void;
}

export default function TemplateBackgroundFields({
  mode,
  gradientFrom,
  gradientTo,
  uploadFile,
  uploadPreviewUrl,
  onModeChange,
  onGradientFromChange,
  onGradientToChange,
  onFileChange,
}: Props) {
  const [livePreview, setLivePreview] = useState<string>("");

  useEffect(() => {
    if (mode !== "gradient") {
      setLivePreview("");
      return;
    }
    setLivePreview(`linear-gradient(180deg, ${gradientFrom}, ${gradientTo})`);
  }, [mode, gradientFrom, gradientTo]);

  return (
    <fieldset className="bg-fields">
      <legend>Фон</legend>
      <div className="bg-mode-tabs" role="tablist" aria-label="Тип фона">
        <button
          type="button"
          role="tab"
          aria-selected={mode === "gradient"}
          className={mode === "gradient" ? "tab active" : "tab"}
          onClick={() => onModeChange("gradient")}
        >
          Градиент
        </button>
        <button
          type="button"
          role="tab"
          aria-selected={mode === "upload"}
          className={mode === "upload" ? "tab active" : "tab"}
          onClick={() => onModeChange("upload")}
        >
          Загрузить
        </button>
      </div>

      {mode === "gradient" ? (
        <div className="gradient-fields">
          <label>
            Сверху
            <input
              type="color"
              value={normalizeHex(gradientFrom)}
              onChange={(e) => onGradientFromChange(e.target.value)}
            />
          </label>
          <label>
            Снизу
            <input
              type="color"
              value={normalizeHex(gradientTo)}
              onChange={(e) => onGradientToChange(e.target.value)}
            />
          </label>
          {livePreview ? (
            <div
              className="bg-preview"
              style={{ background: livePreview }}
              aria-label="Превью градиента"
            />
          ) : null}
        </div>
      ) : (
        <div className="upload-fields">
          <label className="file-label">
            <input
              type="file"
              accept="image/png,image/jpeg"
              onChange={(e) => onFileChange(e.target.files?.[0] ?? null)}
            />
            <span className="file-label-text">
              {uploadFile ? uploadFile.name : "Выбрать PNG/JPG"}
            </span>
          </label>
          <p className="hint">Рекомендуется 1080×1080.</p>
          {uploadPreviewUrl ? (
            <img src={uploadPreviewUrl} alt="Превью фона" className="bg-upload-preview" />
          ) : null}
        </div>
      )}
    </fieldset>
  );
}

function normalizeHex(hex: string): string {
  if (/^#[0-9a-fA-F]{6}$/.test(hex)) return hex;
  if (/^[0-9a-fA-F]{6}$/.test(hex)) return `#${hex}`;
  return "#111111";
}
