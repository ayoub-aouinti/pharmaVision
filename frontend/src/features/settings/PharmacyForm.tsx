import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { api } from '../../api/client'
import type { Pharmacy, PharmacySettings } from '../../api/types'
import { Button, Card } from '../../components/ui'

export default function PharmacyForm({ pharmacy }: { pharmacy: Pharmacy }) {
  const client = useQueryClient()
  const [input, setInput] = useState<PharmacySettings>({
    name: pharmacy.name,
    city: pharmacy.city,
    thumbnailDays: pharmacy.thumbnailDays,
    statisticsMonths: pharmacy.statisticsMonths,
  })
  const save = useMutation({
    mutationFn: () => api.updatePharmacy(pharmacy.id, input),
    onSuccess: () => client.invalidateQueries({ queryKey: ['pharmacies'] }),
  })
  const set = (patch: Partial<PharmacySettings>) => {
    setInput({ ...input, ...patch })
    save.reset()
  }
  const submit = (e: FormEvent) => {
    e.preventDefault()
    // Raccourcir la conservation efface des données : confirmation explicite.
    const shorter = input.statisticsMonths < pharmacy.statisticsMonths
    if (shorter && !window.confirm(
      `Les mesures, ventes et alertes closes de plus de ${input.statisticsMonths} mois seront définitivement effacées. Continuer ?`)) return
    save.mutate()
  }

  return (
    <Card title="Officine et conservation des données">
      <form onSubmit={submit} className="max-w-md space-y-3 text-sm">
        <label className="block font-medium">Nom de l'officine
          <input className="field mt-1" required maxLength={80} value={input.name} onChange={(e) => set({ name: e.target.value })} />
        </label>
        <label className="block font-medium">Ville
          <input className="field mt-1" required maxLength={60} value={input.city} onChange={(e) => set({ city: e.target.value })} />
        </label>
        <label className="block font-medium">Images d'alerte consultables pendant (jours)
          <input className="field mt-1" type="number" required min={1} max={365} value={input.thumbnailDays || ''}
            onChange={(e) => set({ thumbnailDays: Number(e.target.value) })} />
        </label>
        <label className="block font-medium">Mesures, ventes et alertes conservées pendant (mois)
          <input className="field mt-1" type="number" required min={3} max={120} value={input.statisticsMonths || ''}
            onChange={(e) => set({ statisticsMonths: Number(e.target.value) })} />
        </label>
        <p className="text-xs text-muted">
          L'effacement a lieu une fois par jour. Les totaux horaires anonymes ne sont pas concernés. Fuseau horaire :
          {' '}{pharmacy.timeZone}.
        </p>
        {save.error && <p role="alert" className="text-danger">{save.error.message}</p>}
        {save.isSuccess && <p role="status" className="text-brand-dark">Réglages enregistrés.</p>}
        <Button type="submit" variant="primary" disabled={save.isPending}>Enregistrer</Button>
      </form>
    </Card>
  )
}
