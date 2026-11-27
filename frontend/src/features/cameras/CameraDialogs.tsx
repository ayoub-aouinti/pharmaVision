import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { api } from '../../api/client'
import type { Camera, CameraInput, CameraStatus } from '../../api/types'
import { Button, Modal, QueryState } from '../../components/ui'

export function CameraDialog({ pharmacyId, camera, onClose, onSaved }: {
  pharmacyId: number
  camera: Camera | null
  onClose: () => void
  onSaved: (id: number) => void
}) {
  const client = useQueryClient()
  const [input, setInput] = useState<CameraInput>({
    pharmacyId,
    name: camera?.name ?? '',
    brand: camera?.brand ?? 'Hikvision',
    rtspUrl: camera?.rtspUrl ?? 'rtsp://',
    status: camera?.status ?? 'Online',
    deviceId: camera?.deviceId,
  })
  // Réservé au titulaire, comme cette fenêtre.
  const devices = useQuery({ queryKey: ['devices', pharmacyId], queryFn: () => api.devices(pharmacyId) })
  const save = useMutation({
    mutationFn: async () => {
      if (!camera) return api.createCamera(input)
      await api.updateCamera(camera.id, input)
      return camera.id
    },
    onSuccess: async (id) => {
      await client.invalidateQueries({ queryKey: ['cameras', pharmacyId] })
      void client.invalidateQueries({ queryKey: ['devices', pharmacyId] })
      onSaved(id)
      onClose()
    },
  })
  const set = (patch: Partial<CameraInput>) => setInput({ ...input, ...patch })
  const submit = (e: FormEvent) => {
    e.preventDefault()
    save.mutate()
  }

  return (
    <Modal title={camera ? 'Modifier la caméra' : 'Ajouter une caméra'} onClose={onClose}>
      <form onSubmit={submit} className="space-y-3 text-sm">
        <label className="block">Nom
          <input className="field mt-1" required maxLength={80} value={input.name} autoFocus
            onChange={(e) => set({ name: e.target.value })} />
        </label>
        <label className="block">Marque
          <input className="field mt-1" required maxLength={60} value={input.brand}
            onChange={(e) => set({ brand: e.target.value })} />
        </label>
        <label className="block">URL RTSP
          <input className="field mt-1" required pattern="rtsp://.+" value={input.rtspUrl}
            onChange={(e) => set({ rtspUrl: e.target.value })} />
        </label>
        {devices.data && devices.data.length > 1 && (
          <label className="block">Boîtier
            <select className="field mt-1" value={input.deviceId ?? devices.data[0].id}
              onChange={(e) => set({ deviceId: Number(e.target.value) })}>
              {devices.data.map((d) => <option key={d.id} value={d.id}>{d.name}</option>)}
            </select>
          </label>
        )}
        <label className="block">Statut (simulé)
          <select className="field mt-1" value={input.status}
            onChange={(e) => set({ status: e.target.value as CameraStatus })}>
            <option value="Online">En ligne</option>
            <option value="Offline">Hors ligne</option>
            <option value="Unconfigured">Non configurée</option>
          </select>
        </label>
        {save.error && <p role="alert" className="text-danger">{save.error.message}</p>}
        <div className="flex gap-2">
          <Button type="submit" variant="primary" disabled={save.isPending}>Enregistrer</Button>
          <Button onClick={onClose}>Annuler</Button>
        </div>
      </form>
    </Modal>
  )
}

export function DiscoverDialog({ pharmacyId, onClose, onAdded }: {
  pharmacyId: number
  onClose: () => void
  onAdded: (id: number) => void
}) {
  const client = useQueryClient()
  const found = useQuery({
    queryKey: ['discover', pharmacyId],
    queryFn: () => api.discover(pharmacyId),
    refetchInterval: false,
    gcTime: 0,
  })
  const add = useMutation({
    mutationFn: (input: CameraInput) => api.createCamera(input),
    onSuccess: async (id) => {
      await client.invalidateQueries({ queryKey: ['cameras', pharmacyId] })
      onAdded(id)
      onClose()
    },
  })

  return (
    <Modal title="Découverte ONVIF (simulée)" onClose={onClose}>
      {!found.data ? <QueryState error={found.error} /> : found.data.length === 0 ? (
        <p className="text-sm text-muted">Aucune nouvelle caméra sur le réseau.</p>
      ) : (
        <ul className="space-y-2">
          {found.data.map((camera) => (
            <li key={camera.rtspUrl} className="flex flex-wrap items-center gap-2 rounded-btn border border-line p-3">
              <div className="mr-auto min-w-0">
                <p className="text-sm font-semibold">{camera.name}</p>
                <p className="truncate text-xs text-muted">{camera.resolution} · {camera.rtspUrl}</p>
              </div>
              <Button variant="primary" disabled={add.isPending}
                onClick={() => add.mutate({ ...camera, pharmacyId, status: 'Online' })}>
                Ajouter
              </Button>
            </li>
          ))}
        </ul>
      )}
      {add.error && <p role="alert" className="mt-2 text-sm text-danger">{add.error.message}</p>}
    </Modal>
  )
}
