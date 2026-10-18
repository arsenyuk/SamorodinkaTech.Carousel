import { useCallback, useEffect, useState } from "react";
import { fetchAdminStats, type AdminStatsDto } from "../api";

const PERIODS = [7, 30, 90] as const;

export default function StatsPage() {
  const [days, setDays] = useState<number>(30);
  const [stats, setStats] = useState<AdminStatsDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  const load = useCallback(() => {
    setLoading(true);
    setError(null);
    fetchAdminStats(days)
      .then(setStats)
      .catch((e: Error) => setError(e.message))
      .finally(() => setLoading(false));
  }, [days]);

  useEffect(() => {
    load();
  }, [load]);

  return (
    <main className="page">
      {/* Заголовок «Статистика» уже в хедере (текущий режим) — дубль убран */}
      <div className="page-head">
        <div className="period-tabs" role="tablist" aria-label="Период">
          {PERIODS.map((p) => (
            <button
              key={p}
              type="button"
              role="tab"
              aria-selected={days === p}
              className={`tab${days === p ? " active" : ""}`}
              onClick={() => setDays(p)}
            >
              {p} дней
            </button>
          ))}
        </div>
      </div>

      {error ? <p className="error">{error}</p> : null}
      {loading && !stats ? <p className="hint">Загрузка…</p> : null}

      {stats ? (
        <>
          <div className="stats-cards">
            <div className="stats-card">
              <span className="stats-value">{stats.totals.publications}</span>
              <span className="hint">публикаций за {stats.days} дней</span>
            </div>
            <div className="stats-card">
              <span className="stats-value">{stats.totals.starsAmount}</span>
              <span className="hint">Stars получено ({stats.totals.starsCount} оплат)</span>
            </div>
          </div>

          <h3>По дням</h3>
          <table className="stats-table">
            <thead>
              <tr>
                <th>Дата</th>
                <th>Публикаций</th>
                <th>Stars</th>
                <th>Оплат</th>
              </tr>
            </thead>
            <tbody>
              {stats.dayStats.map((d) => (
                <tr key={d.date}>
                  <td>{d.date}</td>
                  <td>{d.publications}</td>
                  <td>{d.starsAmount}</td>
                  <td>{d.starsCount}</td>
                </tr>
              ))}
            </tbody>
          </table>

          <h3>По шаблонам</h3>
          <table className="stats-table">
            <thead>
              <tr>
                <th>Шаблон</th>
                <th>Публикаций</th>
              </tr>
            </thead>
            <tbody>
              {stats.byTemplate.map((t) => (
                <tr key={t.name}>
                  <td>{t.name}</td>
                  <td>{t.count}</td>
                </tr>
              ))}
            </tbody>
          </table>

          <h3>По количеству карточек</h3>
          <table className="stats-table">
            <thead>
              <tr>
                <th>Карточек</th>
                <th>Публикаций</th>
              </tr>
            </thead>
            <tbody>
              {stats.byCardCount.map((b) => (
                <tr key={b.bucket}>
                  <td>{b.bucket === "6+" ? "6 и больше" : b.bucket}</td>
                  <td>{b.count}</td>
                </tr>
              ))}
            </tbody>
          </table>

          <h3>Реклама и изображения</h3>
          <div className="stats-cards">
            <div className="stats-card">
              <span className="stats-value">{stats.counters.withBotAds}</span>
              <span className="hint">с рекламой бота</span>
            </div>
            <div className="stats-card">
              <span className="stats-value">{stats.counters.noAds}</span>
              <span className="hint">без рекламы (отключена)</span>
            </div>
            <div className="stats-card">
              <span className="stats-value">{stats.counters.withOwnImages}</span>
              <span className="hint">со своими изображениями</span>
            </div>
          </div>
        </>
      ) : !loading && !error ? (
        <p className="hint">Нет данных.</p>
      ) : null}
    </main>
  );
}