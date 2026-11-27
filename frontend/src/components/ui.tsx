import { CircleHelp, X } from 'lucide-react'
import { useEffect, useId, useState, type ButtonHTMLAttributes, type ReactNode } from 'react'

export function Card({ title, help, aside, children, className = '' }: {
  title?: string; help?: string; aside?: ReactNode; children: ReactNode; className?: string
}) {
  return (
    <section className={`min-w-0 rounded-card bg-white p-5 ${className}`}>
      {title && (
        <header className="mb-4 flex flex-wrap items-center justify-between gap-3">
          <div className="flex items-center gap-1.5">
            <h2 className="text-lg font-semibold">{title}</h2>
            {help && <Info text={help} />}
          </div>
          {aside}
        </header>
      )}
      {children}
    </section>
  )
}

const VARIANTS = {
  primary: 'bg-brand-dark text-white hover:bg-ink',
  secondary: 'bg-bg text-ink hover:bg-brand-soft',
  danger: 'bg-bg text-danger hover:bg-danger/10',
}

export function Button({ variant = 'secondary', className = '', ...props }:
  ButtonHTMLAttributes<HTMLButtonElement> & { variant?: keyof typeof VARIANTS }) {
  return (
    <button
      type="button"
      className={`inline-flex min-h-9 items-center justify-center gap-1.5 rounded-btn px-3.5 py-1.5 text-sm font-semibold transition-colors disabled:opacity-45 ${VARIANTS[variant]} ${className}`}
      {...props}
    />
  )
}

/** Choix unique parmi quelques options, toutes visibles. */
export function Segmented<T extends string>({ label, options, value, onChange }: {
  label: string
  options: { value: T; label: string }[]
  value: T
  onChange: (value: T) => void
}) {
  return (
    <div role="group" aria-label={label} className="flex w-fit max-w-full overflow-x-auto rounded-btn bg-bg p-1">
      {options.map((o) => (
        <button key={o.value} type="button" aria-pressed={value === o.value} onClick={() => onChange(o.value)}
          className={`rounded-md px-3 py-1 text-sm font-medium whitespace-nowrap transition-colors ${value === o.value ? 'bg-ink text-white' : 'text-muted hover:text-ink'}`}>
          {o.label}
        </button>
      ))}
    </div>
  )
}

/** Explication d'un mot ou d'un chiffre, au survol, au clic ou au clavier. */
export function Info({ text }: { text: string }) {
  const [open, setOpen] = useState(false)
  const id = useId()
  return (
    <span className="relative inline-flex" onMouseEnter={() => setOpen(true)} onMouseLeave={() => setOpen(false)}>
      <button type="button" aria-label="Explication" aria-expanded={open} aria-describedby={open ? id : undefined}
        className="rounded-full text-muted hover:text-brand-dark"
        onClick={(e) => { e.stopPropagation(); setOpen(true) }} onBlur={() => setOpen(false)}
        onKeyDown={(e) => e.key === 'Escape' && setOpen(false)}>
        <CircleHelp size={15} />
      </button>
      {open && (
        <span role="tooltip" id={id}
          className="absolute top-full left-1/2 z-40 mt-1 w-60 -translate-x-1/2 rounded-btn bg-ink p-3 text-left font-sans text-xs leading-relaxed font-normal tracking-normal text-white shadow-xl">
          {text}
        </span>
      )}
    </span>
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
