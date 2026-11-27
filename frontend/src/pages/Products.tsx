import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { Download, TrendingDown, TrendingUp } from 'lucide-react'
import { Bar, BarChart, LabelList, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { api } from '../api/client'
import type { BrandRow, Period, Pharmacy } from '../api/types'
import { DataTable, Stat } from '../components/DataTable'
import { Button, Card, QueryState } from '../components/ui'
import { downloadProducts } from '../lib/exportCsv'
import { formatCount, formatMoney, formatPercent } from '../lib/format'

export default function Products({ pharmacy, period }: { pharmacy: Pharmacy; period: Period }) {
  const report = useQuery({
    queryKey: ['analytics', 'products', pharmacy.id, period],
    queryFn: () => api.productReport(pharmacy.id, period),
    placeholderData: keepPreviousData,
  })
  const data = report.data
  if (!data) return <Card><QueryState error={report.error} /></Card>

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="text-sm text-muted">Ventes issues de la caisse (fictive) ; tendances comparées à la période précédente.</p>
        <Button variant="primary" onClick={() => downloadProducts(pharmacy, period, data)}>
          <Download size={16} /> Exporter (CSV)
        </Button>
      </div>
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <Stat label="Chiffre d’affaires" value={formatMoney(data.revenue)} />
        <Stat label="Unités vendues" value={formatCount(data.units)} />
        <Stat label="Tickets de caisse" value={formatCount(data.tickets)} />
        <Stat label="Marques vendues" value={formatCount(data.brands.length)} />
      </div>
      {data.unreferencedRevenue > 0 && (
        <p role="note" className="rounded-card border border-warn/50 bg-warn/10 px-4 py-3 text-sm">
          {formatMoney(data.unreferencedRevenue)} de ventes portent un code absent du catalogue : elles comptent dans
          leur famille, mais dans aucune marque.
        </p>
      )}

      <Card title="Familles">
        <DataTable empty="Aucune vente sur la période."
          headers={[
            'Famille', 'Chiffre d’affaires', 'Tendance', 'Unités', 'Tickets',
            { label: 'Arrêts', help: 'Arrêts de plus de 5 secondes devant les linéaires de la famille' },
            'Prises en main',
            { label: 'Transformation', help: 'Unités vendues ÷ prises en main ; peut dépasser 100 % (vente au comptoir)' },
          ]}
          rows={data.families.map((f) => ({
            key: f.family,
            cells: [
              <span className="font-medium">{f.family}{!f.hasShelf && <span className="block text-xs font-normal text-muted">Sans linéaire</span>}</span>,
              formatMoney(f.revenue),
              <Trend value={f.revenueTrend} />,
              formatCount(f.units),
              formatCount(f.tickets),
              f.hasShelf ? formatCount(f.stops) : '—',
              f.hasShelf ? formatCount(f.pickups) : '—',
              f.transformation === null ? '—' : formatPercent(f.transformation),
            ],
          }))} />
      </Card>

      <Card title="Marques">
        {data.brands.length > 0 && <BrandChart brands={data.brands} />}
        <p className="my-3 text-xs text-muted">
          La caméra ne reconnaît pas les produits : les arrêts sont mesurés par linéaire et les marques d'un même rayon
          les partagent. La liste des marques d'un linéaire se modifie dans l'écran Caméras.
        </p>
        <DataTable empty="Aucune vente de marque sur la période."
          headers={[
            'Marque', 'Chiffre d’affaires', 'Part', 'Tendance', 'Unités', 'Linéaires',
            { label: 'Arrêts des linéaires', help: 'Total des rayons où la marque est présente (partagé)' },
          ]}
          rows={data.brands.map((b) => ({
            key: b.brand,
            cells: [
              <span className="font-medium">{b.brand}<span className="block text-xs font-normal text-muted">{b.families.join(', ')}</span></span>,
              formatMoney(b.revenue),
              formatPercent(b.revenueShare),
              <Trend value={b.revenueTrend} />,
              formatCount(b.units),
              b.shelves.join(', ') || 'Aucun',
              b.shelves.length ? formatCount(b.shelfStops) : '—',
            ],
          }))} />
      </Card>

      <Card title="Meilleurs produits">
        <DataTable empty="Aucun produit référencé vendu sur la période."
          headers={['Produit', 'Marque', 'Famille', 'Unités', 'Chiffre d’affaires']}
          rows={data.topProducts.map((p) => ({
            key: p.code,
            cells: [
              <span className="font-medium">{p.name}<span className="block text-xs font-normal text-muted">{p.code}</span></span>,
              p.brand, p.family, formatCount(p.units), formatMoney(p.revenue),
            ],
          }))} />
      </Card>
    </div>
  )
}

function Trend({ value }: { value: number | null }) {
  if (value === null) return <span className="text-muted">—</span>
  const Icon = value >= 0 ? TrendingUp : TrendingDown
  return (
    <span className={`inline-flex items-center gap-1 whitespace-nowrap ${value >= 0 ? 'text-brand-dark' : 'text-danger'}`}>
      <Icon size={14} /> {value > 0 ? '+' : ''}{formatPercent(value)}
    </span>
  )
}

function BrandChart({ brands }: { brands: BrandRow[] }) {
  return (
    <figure>
      <figcaption className="mb-2 text-sm font-semibold">Chiffre d’affaires par marque</figcaption>
      <div style={{ height: brands.length * 28 + 8 }}>
        <ResponsiveContainer width="100%" height="100%">
          <BarChart data={brands} layout="vertical" margin={{ top: 0, right: 72, left: 0, bottom: 0 }}>
            <XAxis type="number" hide />
            <YAxis type="category" dataKey="brand" width={110} tickLine={false} axisLine={false} stroke="#0f1a17"
              fontSize={12} interval={0} />
            <Tooltip cursor={{ fill: '#f3f6f5' }} formatter={(v) => [formatMoney(Number(v)), 'Chiffre d’affaires']} />
            <Bar dataKey="revenue" fill="#3d8fd1" radius={[0, 4, 4, 0]} barSize={16} isAnimationActive={false}>
              <LabelList dataKey="revenue" position="right" formatter={(v) => formatMoney(Number(v))}
                fontSize={12} fill="#5b6d66" />
            </Bar>
          </BarChart>
        </ResponsiveContainer>
      </div>
    </figure>
  )
}
