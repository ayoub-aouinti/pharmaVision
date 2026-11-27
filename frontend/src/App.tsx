import { useQuery, useQueryClient } from '@tanstack/react-query'
import {
  Bell, Camera, CircleHelp, Cross, FileText, History, House, LogOut, Package, Settings, SquareDashed,
} from 'lucide-react'
import { useState } from 'react'
import { NavLink, Route, Routes } from 'react-router-dom'
import { api } from './api/client'
import type { CurrentUser, Period, Pharmacy } from './api/types'
import { Button, Card, QueryState, Segmented } from './components/ui'
import { formatTime } from './lib/format'
import { endSession } from './lib/session'
import { useLive } from './lib/useLive'
import Alerts from './pages/Alerts'
import Cameras from './pages/Cameras'
import Dashboard from './pages/Dashboard'
import Help from './pages/Help'
import HistoryPage from './pages/History'
import Login from './pages/Login'
import NotFound from './pages/NotFound'
import Products from './pages/Products'
import Reports from './pages/Reports'
import SettingsPage from './pages/Settings'
import Zones from './pages/Zones'

// Rangés par intention : ce qu'on fait chaque jour, ce qu'on analyse, ce qu'on règle une fois.
const NAV = [
  {
    group: 'Chaque jour',
    items: [
      { to: '/', label: 'Accueil', icon: House },
      { to: '/alertes', label: 'Alertes', icon: Bell },
      { to: '/historique', label: 'Revoir une journée', icon: History },
    ],
  },
  {
    group: 'Analyser',
    items: [
      { to: '/zones', label: 'Rayons & zones', icon: SquareDashed },
      { to: '/produits', label: 'Produits & marques', icon: Package },
      { to: '/rapports', label: 'Rapports', icon: FileText },
    ],
  },
  {
    group: 'Configurer',
    items: [
      { to: '/cameras', label: 'Caméras', icon: Camera },
      { to: '/parametres', label: 'Réglages', icon: Settings },
      { to: '/aide', label: 'Aide', icon: CircleHelp },
    ],
  },
]

const PERIODS: { value: Period; label: string }[] = [
  { value: 'live', label: 'Direct' },
  { value: 'today', label: "Aujourd'hui" },
  { value: '7d', label: '7 jours' },
  { value: '30d', label: '30 jours' },
]

function stored(key: string) {
  try { return localStorage.getItem(key) } catch { return null }
}

export default function App() {
  const me = useQuery({ queryKey: ['me'], queryFn: api.me, staleTime: Infinity, refetchInterval: false })
  if (me.data === undefined) return <div className="p-6"><Card><QueryState error={me.error} /></Card></div>
  return me.data ? <Shell user={me.data} /> : <Login />
}

function Shell({ user }: { user: CurrentUser }) {
  const client = useQueryClient()
  const logout = async () => {
    try { await api.logout() } finally { endSession(client) }
  }
  const pharmacies = useQuery({ queryKey: ['pharmacies'], queryFn: api.pharmacies, refetchInterval: 15_000 })
  const [selected, setSelected] = useState(() => Number(stored('pharmacy')) || 0)
  const [period, setPeriod] = useState<Period>('today')
  const pharmacy = pharmacies.data?.find((p) => p.id === selected) ?? pharmacies.data?.[0]
  const connected = useLive(pharmacy?.id, period)

  const choose = (id: number) => {
    setSelected(id)
    try { localStorage.setItem('pharmacy', String(id)) } catch { /* préférence facultative */ }
  }

  return (
    <div className="flex min-h-screen flex-col lg:flex-row">
      <Sidebar pharmacy={pharmacy} />
      <div className="min-w-0 flex-1">
        <header className="flex flex-wrap items-center gap-x-4 gap-y-3 px-4 pt-4 lg:px-8 lg:pt-6">
          <div className="mr-auto flex flex-wrap items-center gap-2">
            <select aria-label="Pharmacie" className="rounded-btn bg-white px-3 py-2 font-title text-base font-semibold"
              value={pharmacy?.id ?? ''} onChange={(e) => choose(Number(e.target.value))}>
              {pharmacies.data?.map((p) => <option key={p.id} value={p.id}>{p.name} · {p.city}</option>)}
            </select>
            <span className="rounded-full bg-info/15 px-2.5 py-1 text-xs font-semibold text-[#1d5a8c]"
              title="Les chiffres sont simulés : aucune caisse ni caméra réelle par défaut.">
              Démonstration
            </span>
          </div>
          <div className="rounded-btn bg-white">
            <Segmented label="Période" options={PERIODS} value={period} onChange={setPeriod} />
          </div>
          <span role="status" className="flex items-center gap-2 text-sm font-semibold">
            <span className={`size-2.5 rounded-full ${connected ? 'animate-pulse bg-brand' : 'bg-danger'}`} />
            {connected ? 'En direct' : 'Hors connexion'}
          </span>
          <div className="flex items-center gap-2">
            <p className="text-right text-sm leading-tight">
              <span className="block font-semibold">{user.name}</span>
              <span className="text-muted">{user.role === 'Titulaire' ? 'Titulaire' : 'Équipe'}</span>
            </p>
            <Button className="!bg-white" aria-label="Se déconnecter" title="Se déconnecter" onClick={() => void logout()}>
              <LogOut size={16} />
            </Button>
          </div>
        </header>
        <main className="p-4 lg:px-8 lg:py-6">
          {!pharmacy ? <QueryState error={pharmacies.error} /> : (
            <Routes>
              <Route path="/" element={<Dashboard pharmacy={pharmacy} period={period} />} />
              <Route path="/cameras" element={
                <Cameras key={pharmacy.id} pharmacyId={pharmacy.id} canEdit={user.role === 'Titulaire'} />
              } />
              <Route path="/zones" element={<Zones key={pharmacy.id} pharmacy={pharmacy} period={period} />} />
              <Route path="/produits" element={<Products key={pharmacy.id} pharmacy={pharmacy} period={period} />} />
              <Route path="/rapports" element={<Reports key={pharmacy.id} pharmacy={pharmacy} period={period} />} />
              <Route path="/historique" element={<HistoryPage key={pharmacy.id} pharmacy={pharmacy} />} />
              <Route path="/parametres" element={
                <SettingsPage user={user} pharmacy={pharmacy} pharmacies={pharmacies.data ?? []} />
              } />
              <Route path="/aide" element={<Help />} />
              <Route path="/alertes" element={<Alerts key={pharmacy.id} pharmacy={pharmacy} period={period} />} />
              <Route path="*" element={<NotFound />} />
            </Routes>
          )}
        </main>
      </div>
    </div>
  )
}

function Sidebar({ pharmacy }: { pharmacy?: Pharmacy }) {
  const device = pharmacy?.device
  return (
    <aside className="flex shrink-0 flex-col bg-rail text-white lg:sticky lg:top-0 lg:h-screen lg:w-60 lg:overflow-y-auto">
      <div className="flex items-center gap-2.5 px-5 py-4 font-title text-xl font-bold lg:py-6">
        <span className="grid size-8 place-items-center rounded-lg bg-brand text-rail"><Cross size={18} strokeWidth={3} /></span>
        PharmaVision
      </div>
      <nav aria-label="Menu" className="flex gap-1 overflow-x-auto px-3 pb-3 lg:flex-col lg:gap-5">
        {NAV.map((section) => (
          <div key={section.group} className="flex gap-1 lg:flex-col">
            <p className="hidden px-3 pb-1 text-xs font-medium text-white/45 lg:block">{section.group}</p>
            {section.items.map(({ to, label, icon: Icon }) => (
              <NavLink key={to} to={to} end={to === '/'}
                className={({ isActive }) =>
                  `flex items-center gap-3 rounded-btn px-3 py-2 text-sm font-medium whitespace-nowrap transition-colors ${isActive ? 'bg-white text-rail' : 'text-white/75 hover:bg-white/10 hover:text-white'}`}>
                <Icon size={18} />
                {label}
                {to === '/alertes' && !!pharmacy?.activeAlerts && (
                  <span className="ml-auto rounded-full bg-warn px-2 text-xs font-bold text-ink">{pharmacy.activeAlerts}</span>
                )}
              </NavLink>
            ))}
          </div>
        ))}
      </nav>
      {device && (
        <div className="mx-3 mt-auto mb-4 hidden rounded-2xl bg-white/8 px-4 py-3 text-sm lg:block">
          <p className="flex items-center gap-2 font-semibold">
            <span className={`size-2.5 rounded-full ${device.online ? 'bg-brand' : 'bg-danger'}`} />
            {device.name} {device.online ? 'en ligne' : 'hors ligne'}
          </p>
          <p className="mt-1 text-white/60">
            Version {device.version}
            {device.lastSeen && `, vu à ${formatTime(device.lastSeen)}`}
          </p>
        </div>
      )}
    </aside>
  )
}
