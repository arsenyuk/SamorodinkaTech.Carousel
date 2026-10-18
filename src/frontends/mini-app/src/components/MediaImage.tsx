import { useEffect, useState } from "react";
import { fetchMediaBlobUrl } from "../api";

interface Props {
  mediaAssetId: string;
  alt?: string;
}

/** Loads private media via authenticated fetch; plain <img src> would not send JWT. */
export default function MediaImage({ mediaAssetId, alt = "" }: Props) {
  const [url, setUrl] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let revoked: string | null = null;
    let cancelled = false;

    fetchMediaBlobUrl(mediaAssetId)
      .then((blobUrl) => {
        if (cancelled) {
          URL.revokeObjectURL(blobUrl);
          return;
        }
        revoked = blobUrl;
        setUrl(blobUrl);
      })
      .catch((e: Error) => {
        if (!cancelled) setError(e.message);
      });

    return () => {
      cancelled = true;
      if (revoked) URL.revokeObjectURL(revoked);
    };
  }, [mediaAssetId]);

  if (error) {
    return <div className="card-placeholder" title={error} aria-label="Не удалось загрузить изображение" />;
  }

  if (!url) {
    return <div className="card-placeholder" aria-label="Загрузка изображения…" />;
  }

  return <img src={url} alt={alt} loading="lazy" />;
}
