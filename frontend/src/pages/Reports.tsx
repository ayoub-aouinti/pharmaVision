import { useMutation, useQuery } from '@tanstack/react-query'
import { FileSpreadsheet, FileText } from 'lucide-react'
import { useState } from 'react'
import { api } from '../api/client'
import type { Period, Pharmacy, ReportFormat, ReportKind } from '../api/types'
import { Button, Card, QueryState } from '../components/ui'
import { saveBlob } from '../lib/exportCsv'

const REPORTS: { kind: ReportKind; title: string; content: string }[] = [
  {
    kind: 'synthese', title: "Synthèse d'activité",
    content: 'Indicateurs clés et leur évolution, état des linéaires, alertes et interventions, affluence par heure.',
  },
  {
    kind: 'zones', title: 'Rapport par zone',
    content: 'Passages, arrêts, prises en main et attente de chaque zone, détail des linéaires.',
  },
  {
    kind: 'produits', title: 'Produits et marques',
    content: 'Ventes et évolution par famille et par marque, meilleurs produits.',
  },
  {
    kind: 'laboratoire', title: 'Rapport laboratoire',
    content: 'Pour une marque : ventes, produits, visibilité et disponibilité de ses linéaires. Le respect du planogramme n’est pas mesuré.',
  },
]

const PERIOD_LABELS: Record<Period, string> = {
  live: 'la dernière heure', today: "aujourd'hui", '7d': 'les 7 derniers jours', '30d': 'les 30 derniers jours',
}

export default function Reports({ pharmacy, period }: { pharmacy: Pharmacy; period: Period }) {
  const brands = useQuery({ queryKey: ['brands'], queryFn: api.brands })
  const [chosen, setChosen] = useState('')
  const brand = chosen || brands.data?.[0] || ''
  const download = useMutation({
    mutationFn: ({ kind, format }: { kind: ReportKind; format: ReportFormat }) =>
      api.report(kind, pharmacy.id, period, format, kind === 'laboratoire' ? brand : undefined),
    onSuccess: ({ filename, blob }) => saveBlob(filename, blob),
  })
  const busy = (kind: ReportKind, format: ReportFormat) =>
    download.isPending && download.variables.kind === kind && download.variables.format === format

  return (
    <div className="space-y-4">
      <p className="text-sm text-muted">
        Rapports de {pharmacy.name} portant sur <strong>{PERIOD_LABELS[period]}</strong> ; la période se choisit en haut.
      </p>
      {download.error && <p role="alert" className="text-sm text-danger">{download.error.message}</p>}
      <div className="grid gap-4 md:grid-cols-2">
        {REPORTS.map((report) => (
          <Card key={report.kind} title={report.title} className="flex flex-col">
            <p className="mb-3 text-sm text-muted">{report.content}</p>
            {report.kind === 'laboratoire' && (
              brands.data ? (
                <label className="mb-3 block text-sm">
                  <span className="mb-1 block font-medium">Marque</span>
                  <select className="field max-w-xs" value={brand} onChange={(e) => setChosen(e.target.value)}>
                    {brands.data.map((b) => <option key={b}>{b}</option>)}
                  </select>
                </label>
              ) : <QueryState error={brands.error} />
            )}
            <div className="mt-auto flex flex-wrap gap-2">
              {([['pdf', 'PDF', FileText], ['xlsx', 'Excel', FileSpreadsheet]] as const).map(([format, label, Icon]) => (
                <Button key={format} variant={format === 'pdf' ? 'primary' : 'secondary'}
                  aria-label={`${report.title} en ${label}`}
                  disabled={download.isPending || (report.kind === 'laboratoire' && !brand)}
                  onClick={() => download.mutate({ kind: report.kind, format })}>
                  <Icon size={15} /> {busy(report.kind, format) ? 'Préparation…' : label}
                </Button>
              ))}
            </div>
          </Card>
        ))}
      </div>
      <p className="text-xs text-muted">L'envoi automatique hebdomadaire n'est pas encore disponible.</p>
    </div>
  )
}
