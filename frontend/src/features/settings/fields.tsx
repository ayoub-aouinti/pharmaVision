export const MIN_PASSWORD = 10

export function PasswordField({ label, value, onChange, autoComplete, minLength }: {
  label: string
  value: string
  onChange: (value: string) => void
  autoComplete: 'current-password' | 'new-password'
  minLength?: number
}) {
  return (
    <label className="block text-sm font-medium">
      {label}
      <input className="field mt-1" type="password" required maxLength={200} minLength={minLength}
        autoComplete={autoComplete} value={value} onChange={(e) => onChange(e.target.value)} />
    </label>
  )
}
