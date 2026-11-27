import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Download, Trash2, Upload } from 'lucide-react'
import { useState, type ReactNode } from 'react'
import { api } from '../../api/client'
import type { ImportError, Pharmacy, SalesImportReport } from '../../api/types'
import { DataTable } from '../../components/DataTable'
import { Button, Card, QueryState } from '../../components/ui'
import { saveBlob } from '../../lib/exportCsv'
import { formatCount, formatMoney } from '../../lib/format'

const SALES_SAMPLE = 'date;ticket;code;famille;quantite;montant\n'
  + '05/03/2026 09:15;1001;DER-01;Dermo-cosmétique;1;42,000\n'
  + '05/03/2026 09:15;1001;ORD-01;Ordonnance;1;18,500\n'
const CATALOG_SAMPLE = 'code;nom;marque;famille;prix\nDER-01;Crème hydratante 40 ml;Avène;Dermo-cosmétique;42,000\n'

const sample = (name: string, text: string) =>
  saveBlob(name, new Blob(['﻿' + text], { type: 'text/csv;charset=utf-8' }))
const day = (iso: string) => new Date(`${iso}T12:00:00Z`).toLocaleDateString('fr-FR')

export default function ImportPanel({ pharmacy }: { pharmacy: Pharmacy }) {
  return (
    <div className="space-y-4">
      <SalesImportCard pharmacy={pharmacy} />
      <ImportHistory pharmacy={pharmacy} />
      <CatalogCard />
    </div>
  )
}

/** Les écrans qui lisent les ventes doivent se rafraîchir après un changement. */
function useRefresh() {
  const client = useQueryClient()
  return () => Promise.all(['imports', 'analytics', 'kpis', 'shelves', 'hourly', 'history', 'brands']
    .map((key) => client.invalidateQueries({ queryKey: [key] })))
}

function FilePicker({ label, onPick }: { label: string; onPick: (file: File | null) => void }) {
  return (
    <label className="block text-sm font-medium">
      {label}
      <input className="field mt-1" type="file" accept=".csv,.txt,text/csv" onChange={(e) => onPick(e.target.files?.[0] ?? null)} />
    </label>
  )
}

function Errors({ count, errors }: { count: number; errors: ImportError[] }) {
  return (
    <div role="alert" className="rounded-btn border border-danger/40 bg-danger/5 p-3 text-sm">
      <p className="font-semibold text-danger">
        {formatCount(count)} erreur(s) : rien n'a été importé. Corrigez le fichier puis recommencez.
      </p>
      <ul className="mt-1 list-disc pl-5">
        {errors.map((e, i) => <li key={i}>Ligne {e.line} : {e.message}</li>)}
      </ul>
      {count > errors.length && <p className="mt-1 text-muted">… et {formatCount(count - errors.length)} autre(s).</p>}
    </div>
  )
}

function SalesImportCard({ pharmacy }: { pharmacy: Pharmacy }) {
  const refresh = useRefresh()
  const [file, setFile] = useState<File | null>(null)
  const [replace, setReplace] = useState(false)
  const preview = useMutation({ mutationFn: () => api.importSales(pharmacy.id, file!, replace, true) })
  const apply = useMutation({ mutationFn: () => api.importSales(pharmacy.id, file!, replace, false), onSuccess: refresh })
  const change = () => {
    preview.reset()
    apply.reset()
  }
  const report = apply.data ?? preview.data
  const confirm = () => {
    if (report?.replaced && !window.confirm(
      `${formatCount(report.replaced)} vente(s) non importées seront définitivement effacées sur ces jours. Continuer ?`)) return
    apply.mutate()
  }
  const error = apply.error ?? preview.error

  return (
    <Card title={`Ventes de ${pharmacy.name}`} aside={
      <Button onClick={() => sample('modele-ventes.csv', SALES_SAMPLE)}><Download size={15} /> Modèle CSV</Button>
    }>
      <p className="mb-3 text-sm text-muted">
        Une ligne par produit : date et heure, ticket, code, quantité, montant ; la famille est facultative.
        Le fichier est d'abord analysé, sans rien enregistrer.
      </p>
      <div className="max-w-xl space-y-3">
        <FilePicker label="Fichier des ventes" onPick={(f) => { setFile(f); change() }} />
        <label className="flex items-start gap-2 text-sm">
          <input type="checkbox" className="mt-1 accent-brand-dark" checked={replace}
            onChange={(e) => { setReplace(e.target.checked); change() }} />
          <span>Remplacer les ventes non importées (simulées) des jours couverts par le fichier</span>
        </label>
        <Button disabled={!file || preview.isPending || apply.isPending} onClick={() => { change(); preview.mutate() }}>
          {preview.isPending ? 'Analyse…' : 'Analyser le fichier'}
        </Button>
      </div>
      {error && <p role="alert" className="mt-3 text-sm text-danger">{error.message}</p>}
      {report && (
        <div className="mt-4 space-y-3">
          {report.errorCount > 0 ? <Errors count={report.errorCount} errors={report.errors} />
            : <Summary report={report} applied={!!apply.data} />}
          {!apply.data && report.errorCount === 0 && (
            <Button variant="primary" disabled={apply.isPending || (report.newRows === 0 && report.replaced === 0)} onClick={confirm}>
              <Upload size={15} /> {apply.isPending ? 'Import…' : `Importer ${formatCount(report.newRows)} ligne(s)`}
            </Button>
          )}
        </div>
      )}
    </Card>
  )
}

function Summary({ report, applied }: { report: SalesImportReport; applied: boolean }) {
  const facts: [string, ReactNode][] = [
    ['Jours couverts', report.from && report.to ? `${day(report.from)} – ${day(report.to)}` : '—'],
    [applied ? 'Lignes importées' : 'Lignes à importer', formatCount(report.newRows)],
    ['Déjà importées', formatCount(report.duplicates)],
    ['Tickets', formatCount(report.tickets)],
    ['Chiffre d’affaires', formatMoney(report.revenue)],
    [applied ? 'Ventes remplacées' : 'Ventes à remplacer', formatCount(report.replaced)],
  ]
  return (
    <div role="status" className="rounded-btn border border-line bg-bg p-3 text-sm">
      <p className="mb-2 font-semibold">{applied ? 'Import terminé.' : 'Résultat de l’analyse (rien n’est encore enregistré).'}</p>
      <dl className="flex flex-wrap gap-x-8 gap-y-2">
        {facts.map(([label, value]) => (
          <div key={label}><dt className="text-xs text-muted">{label}</dt><dd className="font-medium">{value}</dd></div>
        ))}
      </dl>
      {report.unknownCodes > 0 && (
        <p className="mt-2">
          {formatCount(report.unknownCodes)} code(s) absent(s) du catalogue ({report.unknownExamples.join(', ')}
          {report.unknownCodes > report.unknownExamples.length && '…'}) : ils ne seront rattachés à aucune marque.
        </p>
      )}
      {report.familiesWithoutShelf.length > 0 && (
        <p className="mt-1">Familles sans linéaire du même nom : {report.familiesWithoutShelf.join(', ')}.</p>
      )}
    </div>
  )
}

function ImportHistory({ pharmacy }: { pharmacy: Pharmacy }) {
  const refresh = useRefresh()
  const imports = useQuery({ queryKey: ['imports', pharmacy.id], queryFn: () => api.salesImports(pharmacy.id) })
  const remove = useMutation({ mutationFn: api.deleteImport, onSuccess: refresh })

  return (
    <Card title="Imports précédents">
      {remove.error && <p role="alert" className="mb-2 text-sm text-danger">{remove.error.message}</p>}
      {!imports.data ? <QueryState error={imports.error} /> : (
        <DataTable empty="Aucun fichier importé." headers={['Fichier', 'Importé le', 'Par', 'Jours couverts', 'Lignes', '']}
          rows={imports.data.map((item) => ({
            key: item.id,
            cells: [
              <span className="font-medium break-all">{item.fileName}</span>,
              new Date(item.importedAt).toLocaleString('fr-FR', { timeZone: pharmacy.timeZone }),
              item.importedBy,
              `${day(item.firstDay)} – ${day(item.lastDay)}`,
              formatCount(item.rows),
              <Button variant="danger" aria-label={`Annuler l'import ${item.fileName}`} disabled={remove.isPending}
                onClick={() => window.confirm(`Annuler l'import « ${item.fileName} » et effacer ses ventes ?`) && remove.mutate(item.id)}>
                <Trash2 size={15} /> Annuler
              </Button>,
            ],
          }))} />
      )}
    </Card>
  )
}

function CatalogCard() {
  const refresh = useRefresh()
  const [file, setFile] = useState<File | null>(null)
  const preview = useMutation({ mutationFn: () => api.importCatalog(file!, true) })
  const apply = useMutation({ mutationFn: () => api.importCatalog(file!, false), onSuccess: refresh })
  const change = () => {
    preview.reset()
    apply.reset()
  }
  const report = apply.data ?? preview.data
  const error = apply.error ?? preview.error

  return (
    <Card title="Catalogue des produits" aside={
      <Button onClick={() => sample('modele-catalogue.csv', CATALOG_SAMPLE)}><Download size={15} /> Modèle CSV</Button>
    }>
      <p className="mb-3 text-sm text-muted">
        Commun à toutes les officines. Il rattache les codes de la caisse à une marque et à une famille ; les codes
        connus sont mis à jour, aucun produit n'est supprimé.
      </p>
      <div className="max-w-xl space-y-3">
        <FilePicker label="Fichier du catalogue" onPick={(f) => { setFile(f); change() }} />
        <Button disabled={!file || preview.isPending || apply.isPending} onClick={() => { change(); preview.mutate() }}>
          Analyser le catalogue
        </Button>
      </div>
      {error && <p role="alert" className="mt-3 text-sm text-danger">{error.message}</p>}
      {report && (
        <div className="mt-4 space-y-3">
          {report.errorCount > 0 ? <Errors count={report.errorCount} errors={report.errors} /> : (
            <p role="status" className="text-sm">
              {report.applied ? 'Catalogue mis à jour : ' : 'Analyse : '}
              {formatCount(report.added)} produit(s) nouveau(x), {formatCount(report.updated)} modifié(s) sur {formatCount(report.rows)}.
            </p>
          )}
          {!apply.data && report.errorCount === 0 && (
            <Button variant="primary" disabled={apply.isPending || report.added + report.updated === 0} onClick={() => apply.mutate()}>
              <Upload size={15} /> Mettre à jour le catalogue
            </Button>
          )}
        </div>
      )}
    </Card>
  )
}
