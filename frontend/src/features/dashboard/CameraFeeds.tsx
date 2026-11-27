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
              <div className="relative aspect-video overflow-hidden rounded-btn bg-ink">
                {camera.status === 'Online' ? (
                  <>
                    <img src={snapshotUrl(camera.id)} alt={`Image de ${camera.name}`} className="size-full object-cover" />
                    <ZoneOverlay zones={camera.zones} />
                    <span className="absolute right-1.5 bottom-1.5 flex items-center gap-1 rounded-md bg-ink/80 px-1.5 py-0.5 text-xs text-white">
                      <Users size={12} /> {camera.peopleNow}
                    </span>
                  </>
                ) : (
                  <div className="grid size-full place-content-center justify-items-center gap-1 text-sm text-white/70">
                    <VideoOff /> Hors ligne
                  </div>
                )}
              </div>
              <figcaption className="mt-1 truncate text-xs text-muted">{camera.name}</figcaption>
            </figure>
          ))}
        </div>
      )}
    </Card>
  )
}
