import { useEffect, useState } from "react";
import type { CardDto, CardStyleDto } from "../api";
import { fetchTemplateBackgroundUrl, fetchTemplates } from "../api";
import MediaImage from "./MediaImage";
import { StyledText } from "./StyledText";

interface Props {
  cards: CardDto[];
  templateId?: string | null;
  /** Username бота — ссылка-плашка в правом нижнем углу каждой карточки. */
  botUsername?: string | null;
}

export default function CardCarousel({ cards, templateId, botUsername }: Props) {
  const [defaultStyles, setDefaultStyles] = useState<CardStyleDto[] | null>(null);
  const [bgUrl, setBgUrl] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    let blob: string | null = null;

    if (!templateId) {
      setDefaultStyles(null);
      setBgUrl(null);
      return;
    }

    fetchTemplates()
      .then(async (list) => {
        if (cancelled) return;
        const found = list.find((t) => t.id === templateId);
        setDefaultStyles(found?.defaultStyles ?? null);
        if (!found) return;
        const url = await fetchTemplateBackgroundUrl(found.id);
        if (cancelled) {
          URL.revokeObjectURL(url);
          return;
        }
        blob = url;
        setBgUrl(url);
      })
      .catch((e: Error) => console.error("[carousel] template load failed", e));

    return () => {
      cancelled = true;
      if (blob) URL.revokeObjectURL(blob);
    };
  }, [templateId]);

  if (cards.length === 0) {
    if (!templateId) {
      return <p className="hint">Карточек пока нет.</p>;
    }

    // Шаблон выбран, карточек ещё нет — сразу показываем предв. вид карточки на этом фоне.
    return (
      <div className="carousel" role="list">
        <article
          className="card with-bg card-preview"
          role="listitem"
          style={bgUrl ? { backgroundImage: `url(${bgUrl})` } : undefined}
        >
          {bgUrl ? <div className="card-bg-overlay" /> : <div className="card-placeholder" />}
          <div className="card-content">
            <p>Предпросмотр шаблона — добавьте карточку</p>
          </div>
          {botUsername ? (
            <a
              className="card-bot-link"
              href={`https://t.me/${botUsername}`}
              target="_blank"
              rel="noreferrer"
            >
              t.me/{botUsername}
            </a>
          ) : null}
        </article>
      </div>
    );
  }

  const sorted = [...cards].sort((a, b) => a.order - b.order);
  const hasBg = Boolean(bgUrl);

  return (
    <div className="carousel" role="list">
      {sorted.map((card, index) => (
        <article
          key={card.id}
          className={hasBg ? "card with-bg" : "card"}
          role="listitem"
          style={hasBg ? { backgroundImage: `url(${bgUrl})` } : undefined}
        >
          {hasBg ? (
            <div className="card-bg-overlay" />
          ) : card.mediaAssetId ? (
            <MediaImage mediaAssetId={card.mediaAssetId} alt={`Карточка ${index + 1}`} />
          ) : (
            <div className="card-placeholder" />
          )}
          {hasBg && card.mediaAssetId ? (
            <div className="card-fg-media">
              <MediaImage mediaAssetId={card.mediaAssetId} alt={`Карточка ${index + 1}`} />
            </div>
          ) : null}
          <div className="card-content">
            {card.text ? <StyledText text={card.text} styles={card.styles ?? defaultStyles} /> : null}
            <p className="card-order-hint">#{index + 1}</p>
          </div>
          {botUsername ? (
            <a
              className="card-bot-link"
              href={`https://t.me/${botUsername}`}
              target="_blank"
              rel="noreferrer"
            >
              t.me/{botUsername}
            </a>
          ) : null}
        </article>
      ))}
    </div>
  );
}
