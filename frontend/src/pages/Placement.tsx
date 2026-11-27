import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { Snowflake } from 'lucide-react'
import { useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api/client'
import type { Period, Pharmacy, PlacementProduct, PlacementShelf } from '../api/types'
import { DataTable } from '../components/DataTable'
import PlanGrid from '../components/PlanGrid'
import { Card, QueryState, Segmented } from '../components/ui'
import AdviceList from '../features/placement/AdviceList'
import { TEMPERATURES } from '../features/placement/shared'
import { explain } from '../lib/glossary'
import { formatCount, formatDuration, formatMoney, formatPercent } from '../lib/format'

const LIMITS = [
  { value: '30', label: '30 min' },
  { value: '60', label: '1 h' },
  { value: '120', label: '2 h' },
  { value: '240', label: '4 h' },
]

const minutes = (value: number) => formatDuration(value * 60)

export default function Placement({ pharmacy, period }: { pharmacy: Pharmacy; period: Period }) {
  const [limit, setLimit] = useState('120')
  const report = useQuery({
    queryKey: ['analytics', 'placement', pharmacy.id, period, limit],
    queryFn: () => api.placement(pharmacy.id, period, Number(limit)),
    placeholderData: keepPreviousData,
  })
  const plan = useQuery({
    queryKey: ['heatmap', pharmacy.id, period],
    queryFn: () => api.heatmap(pharmacy.id, period),
  })
  const data = report.data
  if (!data) return <Card><QueryState error={report.error} /></Card>
  const byZone = new Map(data.shelves.map((s) => [s.zoneId, s]))
  const untouched = data.shelves.filter((s) => s.longestUntouchedMinutes >= data.untouchedLimitMinutes)
    .sort((a, b) => b.longestUntouchedMinutes - a.longestUntouchedMinutes)

  return (
    <div className="space-y-5">
      <div>
        <h1 className="text-3xl font-bold lg:text-4xl">Quoi déplacer dans la pharmacie ?</h1>
        <p className="mt-1 max-w-3xl text-muted">
          Les caméras montrent où l'on passe, où l'on s'arrête et quels rayons on touche ; la caisse dit ce qui se vend.
        </p>
      </div>

      {data.notMeasured.length > 0 && (
        <p role="note" className="rounded-2xl bg-warn/15 px-4 py-3 text-sm">
          Non jugés : {data.notMeasured.join(', ')}. Leur boîtier ne détecte pas encore les contacts avec les produits.
        </p>
      )}

      <Card title="Propositions" help="Chaque rayon est comparé aux autres rayons de votre pharmacie. Ce sont des essais à mener, pas des certitudes.">
        <AdviceList advice={data.advice} />
      </Card>

      <div className="grid gap-5 xl:grid-cols-[minmax(0,6fr)_minmax(0,5fr)]">
        <Card title="Rayons chauds et froids" help={explain('hot')}>
          {!plan.data ? <QueryState error={plan.error} /> : (
            <PlanGrid rowHeight="3.4rem" legend={false} cells={plan.data.map((cell) => {
              const shelf = byZone.get(cell.zoneId)
              const t = shelf && TEMPERATURES[shelf.temperature]
              return {
                id: cell.zoneId, x: cell.x, y: cell.y, w: cell.w, h: cell.h, intensity: 0,
                color: t ? { background: t.background, text: t.text } : { background: '#ffffff', text: '#53665f' },
                label: t ? `${cell.name} : ${t.label}, ${shelf.pickups} prises en main, ${shelf.units} unités vendues` : cell.name,
                content: <><span>{cell.name}</span>{t && shelf.temperature !== 'Unknown' && <span className="mt-0.5 font-title text-base font-bold">{t.label}</span>}</>,
              }
            })} />
          )}
          <ul className="mt-3 flex flex-wrap gap-x-4 gap-y-1 text-xs text-muted">
            {Object.values(TEMPERATURES).map((t) => (
              <li key={t.label} className="flex items-center gap-1.5">
                <span className="size-3 rounded" style={{ background: t.background }} />{t.label}
              </li>
            ))}
          </ul>
        </Card>

        <Card title="Rayons sans contact" help={explain('untouched')} aside={
          <Segmented label="Durée sans contact" options={LIMITS} value={limit} onChange={setLimit} />
        }>
          <p className="mb-3 text-sm text-muted">Rayons restés au moins {minutes(Number(limit))} sans qu'aucun produit soit touché, pendant l'ouverture.</p>
          {untouched.length === 0 ? <p className="py-6 text-center text-sm text-muted">Aucun rayon n'est resté aussi longtemps sans contact.</p> : (
            <ul className="space-y-2.5">
              {untouched.map((s) => (
                <li key={s.zoneId} className="flex items-center gap-3 rounded-2xl bg-info/10 p-3">
                  <Snowflake size={22} className="shrink-0 text-[#2f74ad]" />
                  <div className="min-w-0 flex-1">
                    <Link to={`/zones?zone=${s.zoneId}`} className="font-semibold hover:underline">{s.name}</Link>
                    <p className="text-xs text-muted">
                      {formatCount(s.untouchedTimes)} fois sur la période
                      {s.sinceLastTouchMinutes !== null && `. Dernier contact il y a ${minutes(s.sinceLastTouchMinutes)}`}
                    </p>
                  </div>
                  <p className="text-right">
                    <span className="block font-title text-xl font-bold tabular-nums">{minutes(s.longestUntouchedMinutes)}</span>
                    <span className="text-xs text-muted">au plus long</span>
                  </p>
                </li>
              ))}
            </ul>
          )}
        </Card>
      </div>

      <ShelfRanking shelves={data.shelves} />

      <div className="grid gap-5 xl:grid-cols-2">
        <Products title="Médicaments qui partent le mieux" products={data.hotProducts} />
        <Products title="Médicaments qui dorment" products={data.coldProducts} />
      </div>
      <p className="text-xs text-muted">
        Les ventes par médicament viennent de la caisse : la caméra reconnaît un rayon touché, pas la boîte prise.
        Aujourd'hui, les contacts avec les produits sont simulés ; le boîtier réel ne les détecte pas encore.
      </p>
    </div>
  )
}

function ShelfRanking({ shelves }: { shelves: PlacementShelf[] }) {
  return (
    <Card title="Du plus chaud au plus froid">
      <DataTable empty="Aucun rayon. Dessinez-en depuis l'écran Caméras."
        headers={[
          'Rayon', 'Température',
          { label: 'Passages', help: 'Clients passés devant le rayon' },
          { label: 'Arrêts', help: explain('stop') },
          { label: 'Touché', help: explain('pickup') },
          { label: 'Vendu', help: 'Unités de cette famille vendues à la caisse' },
          'Ce qu’on observe',
        ]}
        rows={shelves.map((s) => {
          const t = TEMPERATURES[s.temperature]
          return {
            key: s.zoneId,
            cells: [
              <Link to={`/zones?zone=${s.zoneId}`} className="font-semibold whitespace-nowrap hover:underline">{s.name}</Link>,
              <span className="rounded-full px-2.5 py-1 text-xs font-bold whitespace-nowrap" style={{ background: t.background, color: t.text }}>{t.label}</span>,
              formatCount(s.passes),
              <span className="whitespace-nowrap">{formatCount(s.stops)} <span className="text-xs text-muted">({formatPercent(s.engagement)})</span></span>,
              formatCount(s.pickups),
              formatCount(s.units),
              s.findings.length === 0 ? <span className="text-muted">Rien à signaler</span> : (
                <ul className="text-xs">{s.findings.map((f) => <li key={f.kind}>{f.text}</li>)}</ul>
              ),
            ],
          }
        })} />
    </Card>
  )
}

function Products({ title, products }: { title: string; products: PlacementProduct[] }) {
  return (
    <Card title={title}>
      <DataTable empty="Aucun produit du catalogue dans les familles de vos rayons."
        headers={['Médicament', 'Rayon', 'Vendu', { label: 'Avant', help: 'Unités vendues sur la période précédente' }, 'Montant']}
        rows={products.map((p) => ({
          key: p.code,
          cells: [
            <span className="font-medium">{p.name}<span className="block text-xs font-normal text-muted">{p.brand}</span></span>,
            p.family, formatCount(p.units), formatCount(p.previousUnits), formatMoney(p.revenue),
          ],
        }))} />
    </Card>
  )
}
