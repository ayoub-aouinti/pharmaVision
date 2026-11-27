import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { KeyRound, Plus, Trash2 } from 'lucide-react'
import { useState, type FormEvent } from 'react'
import { api } from '../../api/client'
import type { Device, DeviceKey, Pharmacy } from '../../api/types'
import { DataTable } from '../../components/DataTable'
import { Button, Card, Modal, QueryState } from '../../components/ui'

export default function DevicesPanel({ pharmacy }: { pharmacy: Pharmacy }) {
  const client = useQueryClient()
  const devices = useQuery({ queryKey: ['devices', pharmacy.id], queryFn: () => api.devices(pharmacy.id), refetchInterval: 10_000 })
  const [name, setName] = useState('')
  const [issued, setIssued] = useState<DeviceKey | null>(null)
  const refresh = () => client.invalidateQueries({ queryKey: ['devices', pharmacy.id] })
  const show = async (key: DeviceKey) => {
    setIssued(key)
    setName('')
    await refresh()
  }
  const add = useMutation({ mutationFn: () => api.addDevice(pharmacy.id, name), onSuccess: show })
  const renew = useMutation({ mutationFn: api.renewDeviceKey, onSuccess: show })
  const remove = useMutation({ mutationFn: api.deleteDevice, onSuccess: refresh })
  const error = add.error ?? renew.error ?? remove.error
  const submit = (e: FormEvent) => {
    e.preventDefault()
    add.mutate()
  }
  const seen = (device: Device) => device.lastSeen
    ? new Date(device.lastSeen).toLocaleString('fr-FR', { timeZone: pharmacy.timeZone })
    : 'Jamais connecté'

  return (
    <Card title="Boîtiers locaux">
      <p className="mb-3 text-sm text-muted">
        Chaque boîtier s'identifie avec sa propre clé. Une caméra est rattachée à un boîtier depuis l'écran Caméras.
      </p>
      {error && <p role="alert" className="mb-2 text-sm text-danger">{error.message}</p>}
      {!devices.data ? <QueryState error={devices.error} /> : (
        <DataTable empty="Aucun boîtier." headers={['Boîtier', 'Version', 'Dernier signal', 'Caméras', '']}
          rows={devices.data.map((device) => ({
            key: device.id,
            cells: [
              <span className="font-medium">{device.name}</span>,
              device.version,
              seen(device),
              device.cameras,
              <div className="flex justify-end gap-1.5">
                <Button disabled={renew.isPending}
                  onClick={() => window.confirm(`Remplacer la clé de « ${device.name} » ? Le boîtier sera refusé tant qu'il garde l'ancienne.`) && renew.mutate(device.id)}>
                  <KeyRound size={15} /> Nouvelle clé
                </Button>
                <Button variant="danger" aria-label={`Supprimer ${device.name}`} title={device.cameras ? 'Retirez d’abord ses caméras' : 'Supprimer'}
                  disabled={device.cameras > 0 || remove.isPending}
                  onClick={() => window.confirm(`Supprimer « ${device.name} » ?`) && remove.mutate(device.id)}>
                  <Trash2 size={15} />
                </Button>
              </div>,
            ],
          }))} />
      )}
      <form onSubmit={submit} className="mt-4 flex flex-wrap items-end gap-2">
        <label className="block text-sm font-medium">Nouveau boîtier
          <input className="field mt-1 !w-64" required maxLength={80} placeholder="Nom du boîtier" value={name}
            onChange={(e) => setName(e.target.value)} />
        </label>
        <Button type="submit" variant="primary" disabled={add.isPending}><Plus size={15} /> Ajouter</Button>
      </form>
      {issued && (
        <Modal title="Clé du boîtier" onClose={() => setIssued(null)}>
          <p className="text-sm">Copiez cette clé maintenant : elle ne sera plus affichée.</p>
          <output aria-label="Clé du boîtier" className="mt-2 block rounded-btn bg-bg p-3 font-mono text-sm break-all select-all">
            {issued.key}
          </output>
          <Button variant="primary" className="mt-4" onClick={() => setIssued(null)}>J'ai copié la clé</Button>
        </Modal>
      )}
    </Card>
  )
}
