import { useState } from 'react'
import type { CurrentUser, Pharmacy } from '../api/types'
import AccountForm from '../features/settings/AccountForm'
import DevicesPanel from '../features/settings/DevicesPanel'
import ImportPanel from '../features/settings/ImportPanel'
import PharmacyForm from '../features/settings/PharmacyForm'
import UsersPanel from '../features/settings/UsersPanel'

const TABS = [
  { id: 'account', label: 'Mon compte', ownerOnly: false },
  { id: 'users', label: 'Comptes', ownerOnly: true },
  { id: 'pharmacy', label: 'Officine & données', ownerOnly: true },
  { id: 'devices', label: 'Boîtiers', ownerOnly: true },
  { id: 'import', label: 'Import caisse', ownerOnly: true },
]

export default function Settings({ user, pharmacy, pharmacies }: {
  user: CurrentUser
  pharmacy: Pharmacy
  pharmacies: Pharmacy[]
}) {
  const [tab, setTab] = useState('account')
  const tabs = TABS.filter((t) => !t.ownerOnly || user.role === 'Titulaire')

  return (
    <div className="space-y-4">
      {tabs.length > 1 && (
        <div role="tablist" aria-label="Paramètres" className="flex w-fit max-w-full overflow-x-auto rounded-btn border border-line bg-white p-0.5">
          {tabs.map((t) => (
            <button key={t.id} type="button" role="tab" aria-selected={tab === t.id} onClick={() => setTab(t.id)}
              className={`rounded-md px-3 py-1.5 text-sm whitespace-nowrap ${tab === t.id ? 'bg-ink text-white' : 'text-muted'}`}>
              {t.label}
            </button>
          ))}
        </div>
      )}
      {tab === 'account' && <AccountForm user={user} />}
      {tab === 'users' && <UsersPanel me={user} pharmacies={pharmacies} />}
      {tab === 'pharmacy' && <PharmacyForm key={pharmacy.id} pharmacy={pharmacy} />}
      {tab === 'devices' && <DevicesPanel key={pharmacy.id} pharmacy={pharmacy} />}
      {tab === 'import' && <ImportPanel key={pharmacy.id} pharmacy={pharmacy} />}
    </div>
  )
}
