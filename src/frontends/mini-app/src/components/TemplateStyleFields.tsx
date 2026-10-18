import { Link } from "react-router-dom";
import type { CardStyleDto } from "../api";

const FONTS = ["Inter", "Playfair Display"] as const;
const SIZES = ["S", "M", "L"] as const;
const STYLES = ["Normal", "Bold", "Italic"] as const;

interface Props {
  styles: CardStyleDto[];
  onChange: (styles: CardStyleDto[]) => void;
}

export default function TemplateStyleFields({ styles, onChange }: Props) {
  function update(index: number, patch: Partial<CardStyleDto>) {
    const next = styles.map((s, i) => (i === index ? { ...s, ...patch } : s));
    onChange(next);
  }

  return (
    <fieldset className="style-fields">
      <legend>Стили абзацев (до 3)</legend>
      {styles.map((style, index) => (
        <div key={index} className="style-row">
          <strong>Абзац {index + 1}</strong>
          <label>
            Шрифт
            <select
              value={style.fontName}
              onChange={(e) => update(index, { fontName: e.target.value })}
            >
              {FONTS.map((f) => (
                <option key={f} value={f}>{f}</option>
              ))}
            </select>
          </label>
          <label>
            Цвет
            <input
              type="color"
              value={normalizeColor(style.color)}
              onChange={(e) => update(index, { color: e.target.value })}
            />
          </label>
          <label>
            Размер
            <select
              value={style.fontSize}
              onChange={(e) => update(index, { fontSize: e.target.value })}
            >
              {SIZES.map((s) => (
                <option key={s} value={s}>{s}</option>
              ))}
            </select>
          </label>
          <label>
            Стиль
            <select
              value={style.fontStyle}
              onChange={(e) => update(index, { fontStyle: e.target.value })}
            >
              {STYLES.map((s) => (
                <option key={s} value={s}>{s}</option>
              ))}
            </select>
          </label>
        </div>
      ))}
      <p className="hint">
        <Link to="/templates">Все шаблоны</Link>
      </p>
    </fieldset>
  );
}

function normalizeColor(hex: string): string {
  if (/^#[0-9a-fA-F]{6}$/.test(hex)) return hex;
  if (/^[0-9a-fA-F]{6}$/.test(hex)) return `#${hex}`;
  return "#111111";
}
