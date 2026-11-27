import { Clock3, Hourglass, ShoppingBasket, Star } from 'lucide-react'
import type { ReactNode } from 'react'
import type { HourPoint, ShelfRow } from '../../api/types'
import { Card } from '../../components/ui'
import { formatCount, formatDuration, formatPercent } from '../../lib/format'

/** Ce qu'il faut retenir de la période, en phrases. */
export default function Brief({ shelves, hourly }: { shelves: ShelfRow[]; hourly: HourPoint[] }) {
  const busiest = [...hourly].sort((a, b) => b.visitors - a.visitors)[0]
  const watched = shelves.filter((s) => s.stops > 0)
  const attractive = [...watched].sort((a, b) => b.engagementRate - a.engagementRate)[0]
  const quiet = watched.length > 1 ? [...watched].sort((a, b) => a.engagementRate - b.engagementRate)[0] : undefined
  const empty = [...shelves].sort((a, b) => b.ruptureMinutes - a.ruptureMinutes)[0]

  const facts: { icon: typeof Star; text: ReactNode }[] = []
  if (busiest?.visitors > 0)
    facts.push({ icon: Clock3, text: <>L'heure la plus chargée est <strong>{busiest.hour} h</strong>.</> })
  if (attractive)
    facts.push({
      icon: Star,
      text: <><strong>{attractive.name}</strong> attire le plus : {formatPercent(attractive.engagementRate)} des passants s'arrêtent ({formatCount(attractive.stops)} arrêts).</>,
    })
  if (quiet && quiet.zoneId !== attractive?.zoneId)
    facts.push({
      icon: ShoppingBasket,
      text: <><strong>{quiet.name}</strong> retient le moins : {formatPercent(quiet.engagementRate)} d'arrêts. Sa présentation est peut-être à revoir.</>,
    })
  if (empty?.ruptureMinutes > 0)
    facts.push({
      icon: Hourglass,
      text: <><strong>{empty.name}</strong> est resté presque vide pendant {formatDuration(empty.ruptureMinutes * 60)}.</>,
    })

  return (
    <Card title="À retenir" className="bg-rail text-white">
      {facts.length === 0 ? <p className="text-sm text-white/70">Pas encore assez de mesures sur cette période.</p> : (
        <ul className="space-y-4">
          {facts.map(({ icon: Icon, text }, i) => (
            <li key={i} className="flex gap-3">
              <Icon size={20} className="mt-0.5 shrink-0 text-brand" />
              <p className="text-[0.95rem] leading-relaxed text-white/90">{text}</p>
            </li>
          ))}
        </ul>
      )}
    </Card>
  )
}
