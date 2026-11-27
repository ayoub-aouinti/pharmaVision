import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  Camera as CameraIcon, Minus, MousePointer2, Pencil, Pentagon, Plus, Radar, ShieldOff, Square, Trash2,
} from 'lucide-react'
import { useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { api, snapshotUrl } from '../api/client'
import type { Camera, CameraStatus, Point, Zone, ZoneType } from '../api/types'
import { Button, Card, QueryState } from '../components/ui'
import { CameraDialog, DiscoverDialog } from '../features/cameras/CameraDialogs'
import ZoneCanvas, { type Tool } from '../features/cameras/ZoneCanvas'
import ZoneForm, { rulesFor } from '../features/cameras/ZoneForm'
import { ZONE_TYPES } from '../lib/format'

const STATUS: Record<CameraStatus, { label: string; dot: string }> = {
  Online: { label: 'En ligne', dot: 'bg-brand' },
  Offline: { label: 'Hors ligne', dot: 'bg-danger' },
  Unconfigured: { label: 'Non configurée', dot: 'bg-[#a9b8b2]' },
}

const TOOLS: { tool: Tool; label: string; icon: typeof Square; help: string }[] = [
  { tool: 'select', label: 'Sélection', icon: MousePointer2, help: 'Cliquez une zone ; déplacez ses points pour la modifier.' },
  { tool: 'rectangle', label: 'Rectangle', icon: Square, help: 'Cliquez et faites glisser sur l’image.' },
  { tool: 'polygon', label: 'Polygone', icon: Pentagon, help: 'Un clic par sommet ; recliquez le premier pour fermer (Échap annule).' },
  { tool: 'line', label: 'Ligne de comptage', icon: Minus, help: 'Cliquez le début puis la fin de la ligne.' },
  { tool: 'mask', label: 'Masque de confidentialité', icon: ShieldOff, help: 'Faites glisser sur la partie à ne jamais analyser.' },
]

export default function Cameras({ pharmacyId, canEdit }: { pharmacyId: number; canEdit: boolean }) {
  const client = useQueryClient()
  const cameras = useQuery({ queryKey: ['cameras', pharmacyId], queryFn: () => api.cameras(pharmacyId) })
  // L'écran Zones ouvre directement la caméra d'une zone (?camera=…).
  const [search] = useSearchParams()
  const [cameraId, setCameraId] = useState<number | null>(() => Number(search.get('camera')) || null)
  const [tool, setTool] = useState<Tool>('select')
  const [draft, setDraft] = useState<Zone | null>(null)
  const [dirty, setDirty] = useState(false)
  const [dialog, setDialog] = useState<'add' | 'edit' | 'discover' | null>(null)

  const camera = cameras.data?.find((c) => c.id === cameraId) ?? cameras.data?.[0]

  const refresh = () => Promise.all(
    ['cameras', 'shelves', 'heatmap', 'alerts', 'kpis'].map((key) => client.invalidateQueries({ queryKey: [key] })))
  const close = () => {
    setDraft(null)
    setDirty(false)
    save.reset()
    remove.reset()
  }
  const save = useMutation({
    mutationFn: api.saveZone,
    onSuccess: async (saved) => {
      await refresh()
      setDraft(saved)
      setDirty(false)
    },
  })
  const remove = useMutation({
    mutationFn: api.deleteZone,
    onSuccess: async () => {
      await refresh()
      close()
    },
  })
  const photo = useMutation({ mutationFn: api.referencePhoto, onSuccess: refresh })
  const removeCamera = useMutation({
    mutationFn: api.deleteCamera,
    onSuccess: async () => {
      setCameraId(null)
      close()
      await refresh()
    },
  })

  const canLeave = () => !dirty || window.confirm('Abandonner les modifications non enregistrées de la zone ?')
  const selectCamera = (id: number) => {
    if (!canLeave()) return
    close()
    setTool('select')
    setCameraId(id)
  }
  const selectZone = (zone: Zone) => {
    if (zone.id === draft?.id || !canLeave()) return
    close()
    setDraft(zone)
  }
  const create = (type: ZoneType, polygon: Point[]) => {
    if (!camera || !canLeave()) return
    close()
    setDraft({
      id: 0, cameraId: camera.id, name: type === 'PrivacyMask' ? 'Masque' : '', type, polygon,
      category: null, brands: null, planogramRef: null, facingsExpected: null,
      rules: rulesFor(type, [{ kind: 'LowFill', threshold: 30, durationSec: 0, channel: 'Mobile', enabled: true }]),
    })
    setDirty(true)
    setTool('select')
  }
  const edit = (zone: Zone) => {
    setDraft(zone)
    setDirty(true)
  }

  if (!cameras.data) return <Card><QueryState error={cameras.error} /></Card>

  return (
    <div className="grid gap-4 lg:grid-cols-[16rem_minmax(0,1fr)] 2xl:grid-cols-[18rem_minmax(0,1fr)_22rem]">
      <Card title="Caméras" className="self-start">
        <ul className="space-y-1.5">
          {cameras.data.map((c: Camera) => (
            <li key={c.id}>
              <button type="button" aria-current={c.id === camera?.id} onClick={() => selectCamera(c.id)}
                className={`w-full rounded-btn border p-2.5 text-left ${c.id === camera?.id ? 'border-brand bg-brand/8' : 'border-line hover:border-brand'}`}>
                <span className="block text-sm font-semibold">{c.name}</span>
                <span className="block text-xs text-muted">{c.brand} · {c.protocol} · {c.zones.length} zone(s)</span>
                <span className="mt-1 flex items-center gap-1.5 text-xs">
                  <span className={`size-2 rounded-full ${STATUS[c.status].dot}`} />
                  {STATUS[c.status].label}
                </span>
              </button>
            </li>
          ))}
        </ul>
        {canEdit ? (
          <div className="mt-3 grid gap-2">
            <Button onClick={() => setDialog('discover')}><Radar size={15} /> Découvrir (ONVIF)</Button>
            <Button variant="primary" onClick={() => setDialog('add')}><Plus size={15} /> Ajouter une caméra</Button>
          </div>
        ) : (
          <p className="mt-3 text-xs text-muted">Consultation seule : la configuration est réservée au titulaire.</p>
        )}
      </Card>

      {!camera ? <Card><p className="text-sm text-muted">Ajoutez une caméra pour commencer.</p></Card> : (
        <>
          <Card title={camera.name} className="self-start" aside={canEdit && (
            <div className="flex gap-2">
              <Button onClick={() => setDialog('edit')}><Pencil size={15} /> Modifier</Button>
              <Button variant="danger" disabled={removeCamera.isPending}
                onClick={() => window.confirm(`Supprimer « ${camera.name} », ses zones et leurs mesures ?`) && removeCamera.mutate(camera.id)}>
                <Trash2 size={15} /> Supprimer
              </Button>
            </div>
          )}>
            {canEdit && (<>
            <div role="toolbar" aria-label="Outils de dessin" className="mb-2 flex flex-wrap gap-1.5">
              {TOOLS.map((t) => (
                <Button key={t.tool} aria-pressed={tool === t.tool} variant={tool === t.tool ? 'primary' : 'secondary'}
                  onClick={() => setTool(t.tool)}>
                  <t.icon size={15} /> {t.label}
                </Button>
              ))}
              <Button disabled={camera.status !== 'Online' || photo.isPending} onClick={() => photo.mutate(camera.id)}>
                <CameraIcon size={15} /> Photo de référence
              </Button>
            </div>
            <p className="mb-2 text-xs text-muted">{TOOLS.find((t) => t.tool === tool)?.help}</p>
            </>)}
            <ZoneCanvas key={camera.id} src={snapshotUrl(camera.id, camera.referencePhotoAt)} zones={camera.zones} selected={draft} tool={tool} readOnly={!canEdit}
              onSelect={selectZone} onCreate={create} onChange={(polygon) => draft && edit({ ...draft, polygon })} />
            <p className="mt-2 text-xs text-muted">
              {camera.hasImage ? 'Image envoyée par le boîtier, personnes retirées ou floutées' : 'Image fictive'} · {camera.resolution}{camera.rtspUrl && ` · ${camera.rtspUrl}`}
              {camera.referencePhotoAt && ` · photo de référence du ${new Date(camera.referencePhotoAt).toLocaleString('fr-FR')}`}
            </p>
            {(photo.error ?? removeCamera.error) && (
              <p role="alert" className="mt-1 text-sm text-danger">{(photo.error ?? removeCamera.error)!.message}</p>
            )}
          </Card>

          <Card title={draft ? 'Propriétés de la zone' : 'Zones'} className="self-start lg:col-start-2 2xl:col-start-auto">
            {draft ? (
              <ZoneForm zone={draft} readOnly={!canEdit} dirty={dirty} saving={save.isPending || remove.isPending}
                error={(save.error ?? remove.error)?.message ?? null}
                onChange={edit}
                onSave={() => save.mutate(draft)}
                onCancel={close}
                onDelete={() => window.confirm(`Supprimer la zone « ${draft.name} » et ses mesures ?`) && remove.mutate(draft.id)} />
            ) : camera.zones.length === 0 ? (
              <p className="text-sm text-muted">
                {canEdit ? "Aucune zone. Choisissez un outil et dessinez sur l'image." : 'Aucune zone.'}
              </p>
            ) : (
              <ul className="space-y-1.5">
                {camera.zones.map((zone) => (
                  <li key={zone.id}>
                    <button type="button" onClick={() => selectZone(zone)}
                      className="flex w-full items-center gap-2 rounded-btn border border-line p-2.5 text-left hover:border-brand">
                      <span className="size-3 shrink-0 rounded-sm" style={{ background: ZONE_TYPES[zone.type].color }} />
                      <span className="mr-auto text-sm font-medium">{zone.name}</span>
                      <span className="text-xs text-muted">{ZONE_TYPES[zone.type].label}</span>
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </Card>
        </>
      )}

      {(dialog === 'add' || (dialog === 'edit' && camera)) && (
        <CameraDialog pharmacyId={pharmacyId} camera={dialog === 'edit' ? camera! : null}
          onClose={() => setDialog(null)} onSaved={(id) => { if (dialog === 'add') { close(); setCameraId(id) } }} />
      )}
      {dialog === 'discover' && (
        <DiscoverDialog pharmacyId={pharmacyId} onClose={() => setDialog(null)}
          onAdded={(id) => { close(); setCameraId(id) }} />
      )}
    </div>
  )
}
