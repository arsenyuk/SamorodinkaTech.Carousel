import type { CSSProperties } from "react";
import type { CardStyleDto } from "../api";

export const FONT_FAMILY: Record<string, string> = {
  Inter: "'Inter', system-ui, sans-serif",
  "Playfair Display": "'Playfair Display', Georgia, serif",
};

export const FONT_SIZE: Record<string, string> = {
  S: "14px",
  M: "18px",
  L: "24px",
};

export function paragraphs(text: string): string[] {
  return text
    .replace(/\r\n/g, "\n")
    .split("\n")
    .map((p) => p.trim())
    .filter((p) => p.length > 0);
}

export function styleFor(style: CardStyleDto | null | undefined): CSSProperties {
  if (!style) return {};
  return {
    fontFamily: FONT_FAMILY[style.fontName] ?? undefined,
    color: /^#[0-9a-fA-F]{6}$/.test(style.color) ? style.color : undefined,
    fontSize: FONT_SIZE[style.fontSize],
    fontWeight: style.fontStyle === "Bold" ? 700 : undefined,
    fontStyle: style.fontStyle === "Italic" ? "italic" : undefined,
  };
}

export function StyledText({ text, styles, className }: {
  text: string;
  styles?: CardStyleDto[] | null;
  className?: string;
}) {
  const parts = paragraphs(text);
  if (parts.length === 0) return null;

  return (
    <div className={className ?? "card-text"}>
      {parts.map((paragraph, i) => (
        <p key={i} style={styleFor(styles?.[i])}>
          {paragraph}
        </p>
      ))}
    </div>
  );
}
