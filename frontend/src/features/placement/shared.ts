import { ArrowLeftRight, Eye, Flame, Hand, MessageCircle, MoveRight, PackageOpen } from 'lucide-react'
import type { AdviceKind, Temperature } from '../../api/types'

/** Chaud en rouge-orange, froid en bleu : toujours accompagné du mot, jamais la couleur seule. */
export const TEMPERATURES: Record<Temperature, { label: string; background: string; text: string }> = {
  Hot: { label: 'Chaud', background: '#c9541a', text: '#ffffff' },
  Warm: { label: 'Tiède', background: '#fbdfc7', text: '#6e340b' },
  Cold: { label: 'Froid', background: '#2f74ad', text: '#ffffff' },
  Unknown: { label: 'Pas assez de mesures', background: '#e3e9e7', text: '#53665f' },
}

export const ADVICE: Record<AdviceKind, { icon: typeof Eye; label: string }> = {
  Swap: { icon: ArrowLeftRight, label: 'Échanger' },
  MoveCloser: { icon: MoveRight, label: 'Déplacer' },
  Visibility: { icon: Eye, label: 'Rendre visible' },
  Access: { icon: Hand, label: 'Présenter autrement' },
  Advise: { icon: MessageCircle, label: 'Conseiller' },
  CheckStock: { icon: PackageOpen, label: 'Réassortir' },
  UseHotSpot: { icon: Flame, label: 'Mettre en avant' },
}
