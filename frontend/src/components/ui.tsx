import { X } from 'lucide-react'
import { useEffect, type ButtonHTMLAttributes, type ReactNode } from 'react'

export function Card({ title, aside, children, className = '' }: {
  title?: string; aside?: ReactNode; children: ReactNode; className?: string
}) {
  return (
    <section className={`min-w-0 rounded-card border border-line bg-white p-4 ${className}`}>
      {title && (
        <header className="mb-3 flex flex-wrap items-center justify-between gap-2">
          <h2 className="text-base font-semibold">{title}</h2>
          {aside}
        </header>
      )}
      {children}
    </section>
  )
}

const VARIANTS = {
  primary: 'bg-brand-dark text-white hover:bg-ink',
  secondary: 'border border-line bg-white text-ink hover:border-brand',
  danger: 'border border-line bg-white text-danger hover:border-danger',
}

export function Button({ variant = 'secondary', className = '', ...props }:
  ButtonHTMLAttributes<HTMLButtonElement> & { variant?: keyof typeof VARIANTS }) {
  return (
    <button
      type="button"
      className={`inline-flex items-center justify-center gap-1.5 rounded-btn px-3 py-1.5 text-sm font-medium transition-colors disabled:opacity-50 ${VARIANTS[variant]} ${className}`}
      {...props}
    />
  )
}

export function Modal({ title, onClose, children }: { title: string; onClose: () => void; children: ReactNode }) {
  useEffect(() => {
    const close = (e: KeyboardEvent) => e.key === 'Escape' && onClose()
    window.addEventListener('keydown', close)
    return () => window.removeEventListener('keydown', close)
  }, [onClose])

  return (
    <div className="fixed inset-0 z-50 grid place-items-center bg-ink/60 p-4" onClick={onClose}>
      <div role="dialog" aria-modal="true" aria-label={title}
        className="max-h-full w-full max-w-2xl overflow-auto rounded-card bg-white p-5"
        onClick={(e) => e.stopPropagation()}>
        <header className="mb-4 flex items-center justify-between gap-3">
          <h2 className="text-lg font-semibold">{title}</h2>
          <button type="button" aria-label="Fermer" onClick={onClose}><X size={20} /></button>
        </header>
        {children}
      </div>
    </div>
  )
}

/** États de chargement et d'erreur d'une requête. */
export function QueryState({ error }: { error: Error | null }) {
  return error
    ? <p role="alert" className="py-6 text-center text-sm text-danger">Données indisponibles : {error.message}</p>
    : <p className="py-6 text-center text-sm text-muted">Chargement…</p>
}
