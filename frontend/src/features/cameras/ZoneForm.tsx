import { Trash2 } from 'lucide-react'
import type { FormEvent, ReactNode } from 'react'
import type { AlertKind, Channel, Rule, Zone, ZoneType } from '../../api/types'
import { Button } from '../../components/ui'
import { ZONE_TYPES } from '../../lib/format'

export const CATEGORIES = ['Dermo-cosmétique', 'Bébé', 'Solaires', 'Orthopédie', 'Médicaments conseil', 'Hygiène', 'Compléments']

interface RuleSpec {
  kind: AlertKind
  label: string
  field?: { key: 'threshold' | 'durationSec'; label: string; max: number }
  defaults: Pick<Rule, 'threshold' | 'durationSec'>
}

const RULES: Partial<Record<ZoneType, RuleSpec[]>> = {
  Shelf: [
    {
      kind: 'LowFill', label: 'Seuil de remplissage',
      field: { key: 'threshold', label: 'Alerte sous (%)', max: 100 },
      defaults: { threshold: 30, durationSec: 0 },
    },
    {
      kind: 'Unattended', label: 'Client sans conseil',
      field: { key: 'threshold', label: 'Délai (secondes)', max: 3600 },
      defaults: { threshold: 90, durationSec: 180 },
    },
  ],
  Queue: [{
    kind: 'QueueWait', label: "Attente moyenne au comptoir",
    field: { key: 'threshold', label: 'Alerte au-delà de (secondes)', max: 3600 },
    defaults: { threshold: 240, durationSec: 300 },
  }],
  Restricted: [{
    kind: 'RestrictedPresence', label: 'Présence détectée',
    defaults: { threshold: 0, durationSec: 120 },
  }],
}

/** Règles compatibles avec le type de zone (les autres sont retirées). */
export const rulesFor = (type: ZoneType, rules: Rule[] | null): Rule[] =>
  (rules ?? []).filter((r) => RULES[type]?.some((s) => s.kind === r.kind))

function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <label className="block text-sm">
      <span className="mb-1 block font-medium">{label}</span>
      {children}
    </label>
  )
}

export default function ZoneForm({ zone, readOnly, dirty, saving, error, onChange, onSave, onCancel, onDelete }: {
  zone: Zone
  readOnly?: boolean
  dirty: boolean
  saving: boolean
  error: string | null
  onChange: (zone: Zone) => void
  onSave: () => void
  onCancel: () => void
  onDelete: () => void
}) {
  const set = (patch: Partial<Zone>) => onChange({ ...zone, ...patch })
  const specs = RULES[zone.type] ?? []
  const rules = zone.rules ?? []
  const setRule = (spec: RuleSpec, patch: Partial<Rule>) => {
    const current: Rule = rules.find((r) => r.kind === spec.kind)
      ?? { kind: spec.kind, ...spec.defaults, channel: 'Dashboard', enabled: false }
    set({ rules: [...rules.filter((r) => r.kind !== spec.kind), { ...current, ...patch }] })
  }
  const submit = (e: FormEvent) => {
    e.preventDefault()
    onSave()
  }

  return (
    <form onSubmit={submit} className="space-y-3">
      <fieldset disabled={readOnly} className="space-y-3">
      <Field label="Nom">
        <input className="field" required maxLength={80} value={zone.name} autoFocus={zone.id === 0}
          onChange={(e) => set({ name: e.target.value })} />
      </Field>
      <Field label="Type">
        <select className="field" value={zone.type}
          onChange={(e) => {
            const type = e.target.value as ZoneType
            set({ type, rules: rulesFor(type, zone.rules) })
          }}>
          {Object.entries(ZONE_TYPES)
            // Une ligne a deux points, les autres types une surface.
            .filter(([type]) => (type === 'CountingLine') === (zone.polygon.length === 2))
            .map(([type, { label }]) => <option key={type} value={type}>{label}</option>)}
        </select>
      </Field>

      {zone.type === 'Shelf' && (
        <>
          <Field label="Catégorie">
            <select className="field" value={zone.category ?? ''} onChange={(e) => set({ category: e.target.value || null })}>
              <option value="">— Choisir —</option>
              {CATEGORIES.map((c) => <option key={c}>{c}</option>)}
            </select>
          </Field>
          <Field label="Marques">
            <input className="field" maxLength={200} placeholder="Séparées par des virgules" value={zone.brands ?? ''}
              onChange={(e) => set({ brands: e.target.value || null })} />
          </Field>
          <div className="grid grid-cols-2 gap-2">
            <Field label="Planogramme de référence">
              <input className="field" maxLength={60} value={zone.planogramRef ?? ''}
                onChange={(e) => set({ planogramRef: e.target.value || null })} />
            </Field>
            <Field label="Facings attendus">
              <input className="field" type="number" min={0} max={1000} value={zone.facingsExpected ?? ''}
                onChange={(e) => set({ facingsExpected: e.target.value ? Number(e.target.value) : null })} />
            </Field>
          </div>
        </>
      )}

      {specs.length > 0 && (
        <fieldset className="space-y-2">
          <legend className="mb-1 text-sm font-semibold">Règles d'alerte</legend>
          {specs.map((spec) => {
            const rule = rules.find((r) => r.kind === spec.kind)
            return (
              <div key={spec.kind} className="rounded-btn border border-line p-2.5">
                <label className="flex items-center gap-2 text-sm font-medium">
                  <input type="checkbox" className="accent-brand-dark" checked={rule?.enabled ?? false}
                    onChange={(e) => setRule(spec, { enabled: e.target.checked })} />
                  {spec.label}
                </label>
                {rule?.enabled && (
                  <div className="mt-2 grid grid-cols-2 gap-2">
                    {spec.field && (
                      <Field label={spec.field.label}>
                        <input className="field" type="number" required min={1} max={spec.field.max} value={rule[spec.field.key] || ''}
                          onChange={(e) => setRule(spec, { [spec.field!.key]: Number(e.target.value) })} />
                      </Field>
                    )}
                    <Field label="Notification">
                      <select className="field" value={rule.channel}
                        onChange={(e) => setRule(spec, { channel: e.target.value as Channel })}>
                        <option value="Dashboard">Tableau de bord</option>
                        <option value="Mobile">Mobile</option>
                        <option value="WhatsApp">WhatsApp</option>
                      </select>
                    </Field>
                  </div>
                )}
              </div>
            )
          })}
        </fieldset>
      )}

      </fieldset>

      {error && <p role="alert" className="text-sm text-danger">{error}</p>}
      <div className="flex flex-wrap items-center gap-2 pt-1">
        {!readOnly && (
          <Button type="submit" variant="primary" disabled={!dirty || saving}>
            {saving ? 'Enregistrement…' : 'Enregistrer'}
          </Button>
        )}
        <Button onClick={onCancel}>{dirty ? 'Annuler' : 'Fermer'}</Button>
        {!readOnly && zone.id !== 0 && (
          <Button variant="danger" className="ml-auto" onClick={onDelete} disabled={saving}>
            <Trash2 size={15} /> Supprimer
          </Button>
        )}
      </div>
    </form>
  )
}
