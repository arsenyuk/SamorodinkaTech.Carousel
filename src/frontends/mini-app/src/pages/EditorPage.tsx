import { useCallback, useEffect, useState } from "react";
import { useParams, useSearchParams } from "react-router-dom";
import {
  clearCards,
  fetchPublication,
  fetchTemplates,
  markReady,
  publishToMe,
  type CardDto,
  type CardStyleDto,
  type PublicationDto,
  type UserDto,
} from "../api";
import AppDialog, { type DialogState } from "../components/AppDialog";
import CardCarousel from "../components/CardCarousel";
import StoryPreview from "../components/StoryPreview";
import CardEditor from "../components/CardEditor";

interface Props {
  user: UserDto | null;
  /** Business connection для историй: null = не загружен, false = не подключён. */
  historyConnected: boolean | null;
  /** Username бота — ссылка-плашка на карточках. */
  botUsername: string | null;
  /** Цена публикации в Stars (null = не загружена). */
  starsPrice: number | null;
}

export default function EditorPage({ user, historyConnected, botUsername, starsPrice }: Props) {
  const { id = "" } = useParams();
  const [publication, setPublication] = useState<PublicationDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [templateStyles, setTemplateStyles] = useState<CardStyleDto[] | null>(null);
  const [clearing, setClearing] = useState(false);
  // Растёт после очистки карточек → CardEditor раскрывает выбор шаблона
  const [openTemplateSeq, setOpenTemplateSeq] = useState(0);
  const [message, setMessage] = useState<string | null>(null);

  // Служебные статусы («Карточки удалены…», «опубликовано») не должны висеть вечно
  useEffect(() => {
    if (!message) return;
    const t = setTimeout(() => setMessage(null), 5000);
    return () => clearTimeout(t);
  }, [message]);
  // Превью — штатный режим маршрута (/edit/:id?preview=1): возврат — ссылка «Редактор» в хедере.
  const [searchParams, setSearchParams] = useSearchParams();
  const previewMode = searchParams.get("preview") === "1";
  const [dialog, setDialog] = useState<DialogState | null>(null);

  // Publish block
  const [busy, setBusy] = useState<"me" | null>(null);
  // Крыжек «Отключить рекламу бота» (разовый, на эту публикацию).
  const [noAds, setNoAds] = useState(false);

  // Синк чекбокса с сохранённым выбором публикации (после reload/retry).
  useEffect(() => {
    if (publication) setNoAds(publication.noAds === true);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [publication?.id]);

  // Auth failed mid-session: inside Telegram the Mini App closes itself (no login UI).
  useEffect(() => {
    if (error !== "auth") return;
    const w = window as unknown as { Telegram?: { WebApp?: { close?: () => void } } };
    const webApp = w.Telegram?.WebApp;
    if (webApp?.close) setTimeout(() => webApp.close?.(), 1200);
  }, [error]);

  useEffect(() => {
    if (!id) {
      setError("Missing publication id");
      setLoading(false);
      return;
    }

    if (!user) {
      setError("auth");
      setLoading(false);
      return;
    }

    fetchPublication(id)
      .then(setPublication)
      .catch((e: Error) => {
        const status = (e as Error & { status?: number }).status;
        setError(status === 401 ? "auth" : e.message);
      })
      .finally(() => setLoading(false));
  }, [id, user]);

  useEffect(() => {
    if (!publication?.templateId) {
      setTemplateStyles(null);
      return;
    }
    fetchTemplates()
      .then((list) => {
        const found = list.find((t) => t.id === publication.templateId);
        setTemplateStyles(found?.defaultStyles ?? null);
      })
      .catch(() => setTemplateStyles(null));
  }, [publication?.templateId]);

  const onCardsChanged = useCallback((updater: (prev: CardDto[]) => CardDto[]) => {
    setPublication((prev) => {
      if (!prev) return prev;
      const cards = updater(prev.cards);
      // Последняя карточка удалена: сервер при DELETE сбрасывает шаблон —
      // синхронизируем локально, чтобы предпросмотр/строка «фон» обновились сразу.
      return cards.length === 0 && prev.templateId
        ? { ...prev, cards, templateId: null }
        : { ...prev, cards };
    });
  }, []);

  const onTemplateApplied = useCallback((templateId: string | null) => {
    setPublication((prev) => (prev ? { ...prev, templateId } : prev));
  }, []);

  const onClearAll = useCallback(async () => {
    if (!publication) return;
    if (publication.cards.length === 0) return;
    if (!window.confirm("Удалить все карточки? Действие необратимо.")) return;

    setClearing(true);
    setMessage(null);
    try {
      await clearCards(publication.id);
      // Рефетч: локальный патч оставлял устаревшим templateId (шаблон мог смениться на сервере)
      const fresh = await fetchPublication(publication.id);
      setPublication(fresh);
      // Карточек нет → сразу раскрываем выбор шаблона (сценарий «начать заново»)
      setOpenTemplateSeq((s) => s + 1);
      setMessage("Карточки удалены. Можно начать заново.");
    } catch (e) {
      console.error("[editor] clear cards failed", e);
      setMessage(e instanceof Error ? e.message : "Не удалось очистить карточки.");
    } finally {
      setClearing(false);
    }
  }, [publication]);

  async function onPublishToMe() {
    setBusy("me");
    setMessage(null);
    setDialog(null);
    try {
      try {
        await markReady(id);
      } catch (e) {
        console.warn("[publish] markReady failed (continuing)", e);
      }

      const res = await publishToMe(id, noAds);
      if (res.status === "PaymentRequired" && res.invoiceUrl) {
        const paid = await payWithStars(res.invoiceUrl);
        if (paid) await retryPublishAfterPayment();
        return;
      }
      if (res.status === "Published") {
        showPublished();
        return;
      }
      setDialog({
        kind: "error",
        title: "Ошибка публикации",
        text: `Неожиданный статус: ${res.status}`,
      });
    } catch (e) {
      console.error("[publish] publish-to-me failed", e);
      setDialog({
        kind: "error",
        title: "Ошибка публикации",
        text: e instanceof Error ? e.message : "Не удалось опубликовать карусель.",
      });
    } finally {
      setBusy(null);
    }
  }

  function showPublished() {
    setDialog({
      kind: "success",
      title: "Опубликовано",
      text: "Слайды отправлены в ваши истории Telegram.",
    });
  }

  /** Открывает инвойс Stars. Резолвится true только после статуса 'paid'. */
  function payWithStars(invoiceUrl: string): Promise<boolean> {
    const w = window as unknown as {
      Telegram?: { WebApp?: { openInvoice?: (url: string, cb: (status: string) => void) => void } };
    };
    const openInvoice = w.Telegram?.WebApp?.openInvoice;
    if (!openInvoice) {
      setDialog({
        kind: "error",
        title: "Оплата недоступна",
        text: "Оплата звездами доступна только в Telegram.",
      });
      return Promise.resolve(false);
    }

    return new Promise<boolean>((resolve) => {
      openInvoice(invoiceUrl, (status) => {
        if (status !== "paid") {
          setDialog({
            kind: "error",
            title: "Оплата не завершена",
            text: `Статус оплаты: ${status}`,
          });
          resolve(false);
          return;
        }
        resolve(true);
      });
    });
  }

  /** После оплаты ждём, пока сервер увидит successful_payment (long polling). */
  async function retryPublishAfterPayment(): Promise<void> {
    let lastError: unknown = null;
    for (let attempt = 0; attempt < 6; attempt++) {
      try {
        const res = await publishToMe(id);
        if (res.status === "Published") {
          showPublished();
          return;
        }
        if (res.status !== "PaymentRequired") {
          break;
        }
      } catch (e) {
        lastError = e;
        // Уже опубликовано другим вызовом — для нас это успех.
        if (e instanceof Error && e.message.includes("Cannot publish from status Published")) {
          showPublished();
          return;
        }
      }
      await new Promise((r) => setTimeout(r, 1200));
    }

    setDialog({
      kind: "error",
      title: "Оплата не подтверждена",
      text:
        lastError instanceof Error
          ? lastError.message
          : "Оплата прошла, но публикация не подтвердилась. Повторите публикацию.",
    });
  }

  const onCardError = (text: string) => {
    setDialog({ kind: "error", title: "Ошибка", text });
  };

  if (loading) {
    return <main className="page">Загрузка…</main>;
  }

  if (error === "auth") {
    return <main className="page"><p className="hint">Вход не выполнен.</p></main>;
  }

  if (error) {
    return (
      <main className="page">
        <p className="error">{error}</p>
      </main>
    );
  }

  if (!publication) {
    return <main className="page">Публикация не найдена</main>;
  }

  if (previewMode) {
    return (
      <main className="page preview-clean">
        {/* Режим Превью: реальные байты сервера (рендер/фото + печать), а не HTML-карточки.
            Возврат — штатная ссылка «Редактор» в хедере. */}
        <StoryPreview publication={publication} />
      </main>
    );
  }

  return (
    <main className="page">
      {publication.description ? <p className="description">{publication.description}</p> : null}

      <CardCarousel cards={publication.cards} templateId={publication.templateId} botUsername={noAds || publication.noAds ? null : botUsername} />

      {user ? (
        <>
          <CardEditor
            publicationId={publication.id}
            cards={publication.cards}
            onChanged={onCardsChanged}
            defaultStyles={templateStyles}
            onError={onCardError}
            templateId={publication.templateId}
            onTemplateApplied={onTemplateApplied}
            openTemplatePanelSeq={openTemplateSeq}
            headerActions={
              publication.cards.length > 0 ? (
                <>
                  <div className="card-head-center">
                    <button
                      type="button"
                      className="linkish"
                      onClick={() => setSearchParams({ preview: "1" })}
                      title="Показать карусель как в Telegram"
                    >
                      Превью
                    </button>
                  </div>
                  <div className="card-actions">
                    <button
                      type="button"
                      className="linkish danger"
                      disabled={clearing}
                      onClick={() => void onClearAll()}
                    >
                      {clearing ? "Очистка…" : "Очистить карточки"}
                    </button>
                  </div>
                </>
              ) : null
            }
          />

          <section className="publish-block" aria-label="Публикация">
            <h3>Публикация</h3>

            {publication.cards.length === 0 ? (
              /* Пустая карусель: только подсказка, остальное скрыто до первой карточки */
              <p className="hint">Добавьте хотя бы одну карточку, чтобы опубликовать.</p>
            ) : (
              <>
                {historyConnected === false ? (
                  <p className="hint error">
                    Бот не подключён к вашим историям — публикация не сработает.
                    Подключите: Настройки Telegram → Бизнес → бот.
                  </p>
                ) : null}

                <label className="ads-checkbox">
                  <input
                    type="checkbox"
                    checked={noAds}
                    onChange={(e) => setNoAds(e.target.checked)}
                  />
                  Отключить рекламу бота
                </label>
                <p className="hint">
                  Убирает ссылку на бота из публикации —{" "}
                  {starsPrice != null && starsPrice > 1 ? `${starsPrice} Stars` : "1 Star"}.
                </p>

                <button
                  type="button"
                  disabled={busy !== null}
                  onClick={() => void onPublishToMe()}
                >
                  {busy === "me"
                    ? "Отправка…"
                    : (publication.cards.some((c) => c.mediaAssetId != null) || noAds) && starsPrice != null
                      ? `Опубликовать в мои истории — ${starsPrice > 1 ? `${starsPrice} Stars` : "1 Star"}`
                      : "Опубликовать в мои истории"}
                </button>

                <p className="hint">Карточки с фото и текстовые (шаблон) публикуются в ваших историях Telegram.</p>
              </>
            )}
          </section>
        </>
      ) : (
        <p className="hint">Войдите, чтобы добавлять и править карточки.</p>
      )}

      {message ? <p className="status">{message}</p> : null}
      {dialog ? <AppDialog dialog={dialog} onClose={() => setDialog(null)} /> : null}
    </main>
  );
}
