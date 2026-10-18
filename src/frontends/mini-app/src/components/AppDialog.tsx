export interface DialogState {
  kind: "success" | "error";
  title: string;
  text: string;
}

interface Props {
  dialog: DialogState;
  onClose: () => void;
}

/** Универсальное окно успеха/ошибки в Mini App. */
export default function AppDialog({ dialog, onClose }: Props) {
  return (
    <div
      className="dialog-overlay"
      role="presentation"
      onClick={onClose}
    >
      <div
        className={`dialog ${dialog.kind}`}
        role="dialog"
        aria-modal="true"
        aria-label={dialog.title}
        onClick={(e) => e.stopPropagation()}
      >
        <h3>{dialog.title}</h3>
        <p className="dialog-text">{dialog.text}</p>
        <button type="button" onClick={onClose}>
          Ок
        </button>
      </div>
    </div>
  );
}