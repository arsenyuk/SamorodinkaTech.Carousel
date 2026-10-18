import { useEffect, useState } from "react";
import { fetchStoryImage, type PublicationDto } from "../api";

interface Props {
  publication: PublicationDto;
}

/**
 * Режим «Превью»: показывает РЕАЛЬНЫЕ байты карточек, которые уйдут в Telegram
 * (серверный рендер PNG 1080×1920 / фото с печатью ссылки) — не HTML-раскладку редактора.
 */
export default function StoryPreview({ publication }: Props) {
  const [urls, setUrls] = useState<Record<string, string>>({});

  const cards = publication.cards;

  useEffect(() => {
    let cancelled = false;
    const created: string[] = [];

    (async () => {
      const sorted = [...cards].sort((a, b) => a.order - b.order);
      const entries = await Promise.all(
        sorted.map(async (card) => {
          try {
            const blob = await fetchStoryImage(publication.id, card.id);
            if (cancelled) return null;
            const url = URL.createObjectURL(blob);
            created.push(url);
            return [card.id, url] as const;
          } catch (e) {
            console.error("[story-preview] load failed", e);
            return null;
          }
        }),
      );
      if (!cancelled) {
        setUrls(
          Object.fromEntries(
            entries.filter((e): e is readonly [string, string] => e !== null),
          ),
        );
      }
    })();

    return () => {
      cancelled = true;
      created.forEach((u) => URL.revokeObjectURL(u));
    };
  }, [publication.id, cards]);

  const sorted = [...cards].sort((a, b) => a.order - b.order);
  if (sorted.length === 0) {
    return <p className="hint">Карточек пока нет.</p>;
  }

  const hasPhotoCards = sorted.some((c) => c.mediaAssetId != null && c.text);

  return (
    <>
      <div className="carousel" role="list">
        {sorted.map((card) => (
          <article key={card.id} className="card" role="listitem">
            {urls[card.id] ? (
              <img src={urls[card.id]} alt="" />
            ) : (
              <div className="card-placeholder" aria-label="Загрузка превью…" />
            )}
          </article>
        ))}
      </div>
      {hasPhotoCards ? (
        <p className="hint">
          Фото-карточки: изображение показано как есть; текст уйдёт отдельной подписью (caption).
        </p>
      ) : null}
    </>
  );
}