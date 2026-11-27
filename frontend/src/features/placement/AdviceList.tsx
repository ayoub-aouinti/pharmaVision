import type { Advice } from '../../api/types'
import { ADVICE } from './shared'

/** Les propositions, de la plus utile à la moins urgente : le motif chiffré, puis le geste à faire. */
export default function AdviceList({ advice, compact }: { advice: Advice[]; compact?: boolean }) {
  if (advice.length === 0) {
    return <p className="py-4 text-sm text-muted">Pas encore de proposition : choisissez une période plus longue.</p>
  }
  return (
    <ul className={compact ? 'space-y-3' : 'grid gap-3 lg:grid-cols-2'}>
      {advice.map((item) => {
        const { icon: Icon, label } = ADVICE[item.kind]
        return (
          <li key={`${item.kind}-${item.zoneId}`} className="flex gap-3 rounded-2xl bg-bg p-4">
            <span className="grid size-10 shrink-0 place-items-center rounded-full bg-brand-dark text-white" title={label}>
              <Icon size={19} />
            </span>
            <div className="min-w-0">
              <h3 className="font-semibold">{item.title}</h3>
              <p className="mt-1 text-sm text-muted">{item.reason}</p>
              {!compact && <p className="mt-2 text-sm font-medium text-brand-dark">À faire : {item.action}</p>}
            </div>
          </li>
        )
      })}
    </ul>
  )
}
