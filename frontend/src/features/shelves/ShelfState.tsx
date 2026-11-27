import { AlertTriangle, CircleCheck, CircleHelp, CircleX } from 'lucide-react'
import type { ShelfState } from '../../api/types'

const STATES: Record<ShelfState, { label: string; icon: typeof CircleCheck; badge: string; bar: string }> = {
  Conforme: { label: 'Conforme', icon: CircleCheck, badge: 'bg-brand/12 text-brand-dark', bar: 'bg-brand' },
  StockBas: { label: 'Stock bas', icon: AlertTriangle, badge: 'bg-warn/15 text-[#8a4310]', bar: 'bg-warn' },
  Rupture: { label: 'Rupture', icon: CircleX, badge: 'bg-danger text-white', bar: 'bg-danger' },
  Inconnu: { label: 'Sans mesure', icon: CircleHelp, badge: 'bg-bg text-muted', bar: 'bg-line' },
}

export function FillBar({ fill, state }: { fill: number | null; state: ShelfState }) {
  return (
    <div className="flex items-center gap-2">
      <div className="h-2 w-12 overflow-hidden rounded-full bg-bg">
        <div className={`h-full rounded-full ${STATES[state].bar}`} style={{ width: `${fill ?? 0}%` }} />
      </div>
      <span className="w-11 text-right">{fill === null ? '—' : `${Math.round(fill)} %`}</span>
    </div>
  )
}

export function ShelfBadge({ state }: { state: ShelfState }) {
  const { label, icon: Icon, badge } = STATES[state]
  return (
    <span className={`inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-semibold whitespace-nowrap ${badge}`}>
      <Icon size={13} /> {label}
    </span>
  )
}
