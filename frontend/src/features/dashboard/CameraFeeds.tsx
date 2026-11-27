import type { UseQueryResult } from '@tanstack/react-query'
import { Users, VideoOff } from 'lucide-react'
import { Link } from 'react-router-dom'
import { snapshotUrl } from '../../api/client'
import type { Camera } from '../../api/types'
import { Card, QueryState } from '../../components/ui'
import ZoneOverlay from '../cameras/ZoneOverlay'

export default function CameraFeeds({ query, className }: { query: UseQueryResult<Camera[]>; className?: string }) {
  const cameras = query.data?.filter((c) => c.status !== 'Unconfigured')
  return (
    <Card title="Flux caméras" className={className}
      aside={<Link to="/cameras" className="text-sm font-medium text-brand-dark hover:underline">Gérer les zones</Link>}>
      {!cameras ? <QueryState error={query.error} /> : (
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
          {cameras.map((camera) => (
            <figure key={camera.id}>
              {camera.status === 'Online' ? (
                // La hauteur suit l'image : les zones restent alignées quelles que soient ses proportions.
                <div className="relative overflow-hidden rounded-btn bg-ink">
                  <img src={snapshotUrl(camera.id, camera.referencePhotoAt)} alt={`Image de ${camera.name}`} className="block w-full" />
                  <ZoneOverlay zones={camera.zones} />
                  <span className="absolute right-1.5 bottom-1.5 flex items-center gap-1 rounded-md bg-ink/80 px-1.5 py-0.5 text-xs text-white">
                    <Users size={12} /> {camera.peopleNow}
                  </span>
                </div>
              ) : (
                <div className="grid aspect-video place-content-center justify-items-center gap-1 rounded-btn bg-ink text-sm text-white/70">
                  <VideoOff /> Hors ligne
                </div>
              )}
              <figcaption className="mt-1 truncate text-xs text-muted">{camera.name}</figcaption>
            </figure>
          ))}
        </div>
      )}
    </Card>
  )
}
