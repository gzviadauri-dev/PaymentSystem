import { useEffect } from 'react'
import { useToastStore, type Toast } from '../store/toastStore'

const ICONS: Record<Toast['type'], string> = {
  success: '✓',
  error: '✕',
  info: 'ℹ',
}

const STYLES: Record<Toast['type'], string> = {
  success: 'bg-green-50 border-green-300 text-green-800',
  error:   'bg-red-50   border-red-300   text-red-800',
  info:    'bg-blue-50  border-blue-300  text-blue-800',
}

const ICON_STYLES: Record<Toast['type'], string> = {
  success: 'bg-green-100 text-green-600',
  error:   'bg-red-100   text-red-600',
  info:    'bg-blue-100  text-blue-600',
}

const AUTO_DISMISS_MS = 5000

function ToastItem({ toast }: { toast: Toast }) {
  const remove = useToastStore((s) => s.remove)

  useEffect(() => {
    const timer = setTimeout(() => remove(toast.id), AUTO_DISMISS_MS)
    return () => clearTimeout(timer)
  }, [toast.id, remove])

  return (
    <div
      className={`flex items-start gap-3 px-4 py-3 rounded-xl border shadow-lg max-w-sm w-full animate-fade-in ${STYLES[toast.type]}`}
    >
      <span
        className={`mt-0.5 flex-shrink-0 w-6 h-6 rounded-full flex items-center justify-center text-xs font-bold ${ICON_STYLES[toast.type]}`}
      >
        {ICONS[toast.type]}
      </span>
      <div className="flex-1 min-w-0">
        <p className="font-semibold text-sm">{toast.title}</p>
        {toast.message && (
          <p className="text-xs mt-0.5 opacity-80">{toast.message}</p>
        )}
      </div>
      <button
        onClick={() => remove(toast.id)}
        className="flex-shrink-0 opacity-50 hover:opacity-100 text-lg leading-none"
      >
        ×
      </button>
    </div>
  )
}

export default function ToastContainer() {
  const toasts = useToastStore((s) => s.toasts)

  if (toasts.length === 0) return null

  return (
    <div className="fixed bottom-5 right-5 z-50 flex flex-col gap-2 items-end">
      {toasts.map((t) => (
        <ToastItem key={t.id} toast={t} />
      ))}
    </div>
  )
}
