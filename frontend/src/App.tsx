import { useQuery, useQueryClient } from '@tanstack/react-query'
import {
  BarChart3, Bell, Camera, FileText, History, LayoutDashboard, LogOut, Package, Settings, SquareDashed,
} from 'lucide-react'
import { useState } from 'react'
import { NavLink, Route, Routes } from 'react-router-dom'
import { api } from './api/client'
import type { CurrentUser, Period, Pharmacy } from './api/types'
import { Button, Card, QueryState } from './components/ui'
import { formatTime } from './lib/format'
import { endSession } from './lib/session'
import { useLive } from './lib/useLive'
import Alerts from './pages/Alerts'
import Cameras from './pages/Cameras'
import NotFound from './pages/NotFound'
import Dashboard from './pages/Dashboard'
import HistoryPage from './pages/History'
import Login from './pages/Login'
import Products from './pages/Products'
import Reports from './pages/Reports'
import SettingsPage from './pages/Settings'
import Zones from './pages/Zones'

const NAV = [
  { to: '/', label: 'Tableau de bord', icon: LayoutDashboard },
  { to: '/cameras', label: 'Caméras', icon: Camera },
  { to: '/zones', label: 'Zones & linéaires', icon: SquareDashed },
  { to: '/produits', label: 'Produits & marques', icon: Package },
  { to: '/alertes', label: 'Alertes', icon: Bell },
  { to: '/historique', label: 'Historique & replay', icon: History },
  { to: '/rapports', label: 'Rapports', icon: FileText },
  { to: '/parametres', label: 'Paramètres', icon: Settings },
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
        <header className="flex flex-wrap items-center gap-3 border-b border-line bg-white px-4 py-3 lg:px-6">
          <div className="mr-auto">
            <select aria-label="Pharmacie" className="-ml-1 bg-transparent font-title text-xl font-semibold"
              value={pharmacy?.id ?? ''} onChange={(e) => choose(Number(e.target.value))}>
              {pharmacies.data?.map((p) => <option key={p.id} value={p.id}>{p.name} · {p.city}</option>)}
            </select>
            <p className="text-sm text-muted">
              {new Date().toLocaleDateString('fr-FR', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' })}
              {' · '}données fictives
            </p>
          </div>
          <div role="group" aria-label="Période" className="flex rounded-btn border border-line p-0.5">
            {PERIODS.map((p) => (
              <button key={p.value} type="button" aria-pressed={period === p.value} onClick={() => setPeriod(p.value)}
                className={`rounded-md px-3 py-1 text-sm ${period === p.value ? 'bg-ink text-white' : 'text-muted'}`}>
                {p.label}
              </button>
            ))}
          </div>
          <span role="status" className="flex items-center gap-2 text-sm font-medium">
            <span className={`size-2.5 rounded-full ${connected ? 'animate-pulse bg-brand' : 'bg-danger'}`} />
            {connected ? 'En direct' : 'Hors connexion'}
          </span>
          <div className="flex items-center gap-2 border-l border-line pl-3">
            <p className="text-sm leading-tight">
              <span className="block font-medium">{user.name}</span>
              <span className="text-muted">{user.role === 'Titulaire' ? 'Titulaire' : 'Équipe'}</span>
            </p>
            <Button aria-label="Se déconnecter" title="Se déconnecter" onClick={() => void logout()}>
              <LogOut size={16} />
            </Button>
          </div>
        </header>
        <main className="p-4 lg:p-6">
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
    <aside className="flex shrink-0 flex-col bg-ink text-white lg:sticky lg:top-0 lg:h-screen lg:w-60">
      <div className="flex items-center gap-2 px-5 py-4 font-title text-xl font-bold">
        <BarChart3 className="text-brand" /> PharmaVision
      </div>
      <nav className="flex gap-1 overflow-x-auto px-3 pb-2 lg:flex-col">
        {NAV.map(({ to, label, icon: Icon }) => (
          <NavLink key={to} to={to} end={to === '/'}
            className={({ isActive }) =>
              `flex items-center gap-3 rounded-btn px-3 py-2 text-sm whitespace-nowrap ${isActive ? 'bg-brand-dark' : 'text-white/70 hover:bg-white/10'}`}>
            <Icon size={18} />
            {label}
            {to === '/alertes' && !!pharmacy?.activeAlerts && (
              <span className="ml-auto rounded-full bg-warn px-2 text-xs font-semibold text-ink">
                {pharmacy.activeAlerts}
              </span>
            )}
          </NavLink>
        ))}
      </nav>
      {device && (
        <div className="mt-auto hidden border-t border-white/10 px-5 py-4 text-sm lg:block">
          <p className="flex items-center gap-2 font-medium">
            <span className={`size-2.5 rounded-full ${device.online ? 'bg-brand' : 'bg-danger'}`} />
            {device.name} {device.online ? 'en ligne' : 'hors ligne'}
          </p>
          <p className="mt-1 text-white/60">
            Version {device.version}
            {device.lastSeen && ` · vu à ${formatTime(device.lastSeen)}`}
          </p>
        </div>
      )}
    </aside>
  )
}
