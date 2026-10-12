import { useTranslation } from 'react-i18next'
import { cn } from '@/lib/utils'
import type { DentalConditionDto, ToothConditionType, ToothDto } from '@/lib/types'

export const PERMANENT_UPPER = [18, 17, 16, 15, 14, 13, 12, 11, 21, 22, 23, 24, 25, 26, 27, 28]
export const PERMANENT_LOWER = [48, 47, 46, 45, 44, 43, 42, 41, 31, 32, 33, 34, 35, 36, 37, 38]
export const PRIMARY_UPPER = [55, 54, 53, 52, 51, 61, 62, 63, 64, 65]
export const PRIMARY_LOWER = [85, 84, 83, 82, 81, 71, 72, 73, 74, 75]

/** Visual encoding: every condition has a fill AND a glyph, so colour never carries meaning alone. */
export const conditionStyle: Record<ToothConditionType, { fill: string; glyph: 'dot' | 'x' | 'line' | 'screw' | 'zigzag' | 'ring' | 'bar' | 'none' }> = {
  Caries: { fill: 'var(--critical)', glyph: 'dot' },
  Filling: { fill: 'var(--series-1)', glyph: 'none' },
  Crown: { fill: 'var(--series-4)', glyph: 'ring' },
  RootCanal: { fill: 'var(--recovery-from)', glyph: 'line' },
  Extraction: { fill: 'var(--border-strong)', glyph: 'x' },
  Missing: { fill: 'var(--border-strong)', glyph: 'x' },
  Implant: { fill: 'var(--series-3)', glyph: 'screw' },
  Fracture: { fill: 'var(--serious)', glyph: 'zigzag' },
  Bridge: { fill: 'var(--series-5)', glyph: 'bar' },
  Veneer: { fill: 'var(--primary)', glyph: 'none' },
  Sealant: { fill: 'var(--good)', glyph: 'none' },
  Other: { fill: 'var(--text-3)', glyph: 'dot' },
}

const precedence: ToothConditionType[] = ['Missing', 'Extraction', 'Implant', 'Caries', 'Fracture', 'RootCanal', 'Crown', 'Bridge', 'Veneer', 'Filling', 'Sealant', 'Other']

export const activeConditions = (tooth?: ToothDto) => (tooth?.conditions ?? []).filter((c) => c.status !== 'Resolved')

function primaryCondition(conds: DentalConditionDto[]) {
  return [...conds].sort((a, b) => precedence.indexOf(a.type) - precedence.indexOf(b.type))[0]
}

const toothWidth = (n: number) => {
  const unit = n % 10
  if (n >= 50) return unit >= 4 ? 40 : 32
  return unit >= 6 ? 46 : unit >= 4 ? 38 : unit === 3 ? 34 : 32
}
const rootCount = (n: number) => {
  const unit = n % 10
  if (n >= 50) return unit >= 4 ? 2 : 1
  return unit >= 6 ? (n < 30 ? 3 : 2) : unit === 4 && n < 30 ? 2 : 1
}

function Tooth({ number, tooth, upper, selected, onClick, x }: {
  number: number; tooth?: ToothDto; upper: boolean; selected: boolean; onClick: () => void; x: number
}) {
  const { t } = useTranslation()
  const conds = activeConditions(tooth)
  const main = primaryCondition(conds)
  const style = main ? conditionStyle[main.type] : null
  const planned = main && (main.status === 'Planned' || main.status === 'Diagnosed') && main.type !== 'Caries' && main.type !== 'Fracture'
  const w = toothWidth(number)
  const crownH = 34
  const rootH = 38
  const absent = main && (main.type === 'Missing' || main.type === 'Extraction') && main.status !== 'Planned'
  const roots = rootCount(number)

  // Local geometry: crown sits toward the occlusal plane (middle of the chart); roots point away.
  const crownY = upper ? rootH : 0
  const rootY0 = upper ? rootH : crownH
  const rootPaths = Array.from({ length: roots }).map((_, i) => {
    const seg = w / roots
    const x0 = i * seg + 4, x1 = (i + 1) * seg - 4, mid = (x0 + x1) / 2
    const tip = upper ? 2 : crownH + rootH - 2
    return `M ${x0} ${rootY0} Q ${mid - 2} ${(rootY0 + tip) / 2} ${mid} ${tip} Q ${mid + 2} ${(rootY0 + tip) / 2} ${x1} ${rootY0} Z`
  })
  const label = `${number}${main ? ` — ${conds.map((c) => t(`enum.ToothConditionType.${c.type}`)).join(', ')}` : ''}`

  return (
    <g
      transform={`translate(${x}, 0)`}
      onClick={onClick}
      onKeyDown={(e) => (e.key === 'Enter' || e.key === ' ') && onClick()}
      role="button"
      tabIndex={0}
      aria-label={label}
      aria-pressed={selected}
      className="cursor-pointer outline-none [&:focus-visible>rect.focus]:stroke-[var(--primary)]"
    >
      <title>{label}</title>
      <rect className="focus" x={-3} y={-3} width={w + 6} height={crownH + rootH + 6} rx={10} fill={selected ? 'color-mix(in srgb, var(--primary) 12%, transparent)' : 'transparent'}
        stroke={selected ? 'var(--primary)' : 'transparent'} strokeWidth={2} />
      <g opacity={absent ? 0.35 : 1}>
        {rootPaths.map((d, i) => (
          <path key={i} d={d} fill="var(--surface-2)" stroke="var(--border-strong)" strokeWidth={1.2} />
        ))}
        {style?.glyph === 'line' && rootPaths.map((_, i) => {
          const seg = w / roots, mid = i * seg + seg / 2
          return <line key={i} x1={mid} x2={mid} y1={rootY0} y2={upper ? 6 : crownH + rootH - 6} stroke={style.fill} strokeWidth={3} strokeLinecap="round" />
        })}
        {style?.glyph === 'screw' && [0.25, 0.45, 0.65, 0.85].map((f) => {
          const y = upper ? rootY0 - f * rootH : rootY0 + f * rootH
          return <line key={f} x1={w * 0.3} x2={w * 0.7} y1={y} y2={y} stroke={style.fill} strokeWidth={2.5} strokeLinecap="round" />
        })}
        <rect
          x={1} y={crownY} width={w - 2} height={crownH} rx={9}
          fill={style && !absent ? (planned ? 'var(--surface)' : `color-mix(in srgb, ${style.fill} 28%, var(--surface))`) : 'var(--surface)'}
          stroke={style ? style.fill : 'var(--border-strong)'}
          strokeWidth={style ? 2 : 1.2}
          strokeDasharray={planned ? '4 3' : undefined}
        />
        {style?.glyph === 'ring' && <rect x={5} y={crownY + 4} width={w - 10} height={crownH - 8} rx={6} fill="none" stroke={style.fill} strokeWidth={2} />}
        {style?.glyph === 'dot' && <circle cx={w / 2} cy={crownY + crownH / 2} r={6} fill={style.fill} />}
        {style?.glyph === 'zigzag' && (
          <polyline points={`${w * 0.2},${crownY + 8} ${w * 0.45},${crownY + crownH / 2} ${w * 0.35},${crownY + crownH / 2} ${w * 0.7},${crownY + crownH - 6}`}
            fill="none" stroke={style.fill} strokeWidth={2.5} strokeLinejoin="round" />
        )}
        {style?.glyph === 'bar' && <rect x={0} y={crownY + crownH / 2 - 2} width={w} height={4} fill={style.fill} />}
      </g>
      {absent && (
        <g stroke="var(--critical)" strokeWidth={2.5} strokeLinecap="round">
          <line x1={6} y1={crownY + 4} x2={w - 6} y2={crownY + crownH - 4} />
          <line x1={w - 6} y1={crownY + 4} x2={6} y2={crownY + crownH - 4} />
        </g>
      )}
      {conds.length > 1 && (
        <g>
          <circle cx={w - 4} cy={upper ? crownY + crownH - 2 : crownY + 2} r={7} fill="var(--text)" />
          <text x={w - 4} y={(upper ? crownY + crownH - 2 : crownY + 2) + 3.5} textAnchor="middle" fontSize={9} fontWeight={700} fill="var(--surface)">{conds.length}</text>
        </g>
      )}
      <text x={w / 2} y={upper ? crownY + crownH + 16 : -8} textAnchor="middle" fontSize={11} fontWeight={600}
        fill={selected ? 'var(--primary)' : 'var(--text-2)'} className="tabular">{number}</text>
    </g>
  )
}

function Arch({ numbers, teeth, upper, selected, onSelect }: {
  numbers: number[]; teeth: Map<number, ToothDto>; upper: boolean; selected?: number; onSelect: (n: number) => void
}) {
  const gap = 6
  const midGap = 18
  let x = 0
  const positions = numbers.map((n, i) => {
    const pos = x
    x += toothWidth(n) + gap + (i === numbers.length / 2 - 1 ? midGap : 0)
    return pos
  })
  const width = x - gap
  const height = 34 + 38 + 22
  return (
    <svg viewBox={`-6 ${upper ? -6 : -24} ${width + 12} ${height + 8}`} className="w-full" style={{ maxHeight: 150, direction: 'ltr' }}>
      <line x1={positions[numbers.length / 2] - midGap / 2 - gap / 2} x2={positions[numbers.length / 2] - midGap / 2 - gap / 2}
        y1={upper ? -4 : -20} y2={upper ? 90 : 76} stroke="var(--border)" strokeDasharray="3 4" />
      {numbers.map((n, i) => (
        <Tooth key={n} number={n} tooth={teeth.get(n)} upper={upper} selected={selected === n} onClick={() => onSelect(n)} x={positions[i]} />
      ))}
    </svg>
  )
}

export function Odontogram({ teeth, primary, selected, onSelect }: {
  teeth: ToothDto[]; primary: boolean; selected?: number; onSelect: (tooth: number) => void
}) {
  const { t } = useTranslation()
  const map = new Map(teeth.map((x) => [x.toothNumber, x]))
  const upper = primary ? PRIMARY_UPPER : PERMANENT_UPPER
  const lower = primary ? PRIMARY_LOWER : PERMANENT_LOWER
  return (
    <div className="space-y-2">
      <div className="flex justify-between px-1 text-[11px] font-medium tracking-wide text-ink-3 uppercase" dir="ltr">
        <span>{t('chart.right')}</span><span>{t('chart.upper')}</span><span>{t('chart.left')}</span>
      </div>
      <Arch numbers={upper} teeth={map} upper selected={selected} onSelect={onSelect} />
      <div className="mx-auto h-px w-full bg-line" />
      <Arch numbers={lower} teeth={map} upper={false} selected={selected} onSelect={onSelect} />
      <div className="flex justify-between px-1 text-[11px] font-medium tracking-wide text-ink-3 uppercase" dir="ltr">
        <span>{t('chart.right')}</span><span>{t('chart.lower')}</span><span>{t('chart.left')}</span>
      </div>
    </div>
  )
}

export function ChartLegendBar() {
  const { t } = useTranslation()
  const types: ToothConditionType[] = ['Caries', 'Filling', 'Crown', 'RootCanal', 'Implant', 'Missing', 'Fracture', 'Bridge', 'Veneer', 'Sealant']
  return (
    <div className="flex flex-wrap gap-x-4 gap-y-2 text-xs text-ink-2">
      {types.map((type) => (
        <span key={type} className="inline-flex items-center gap-1.5">
          <svg width="14" height="14" viewBox="0 0 14 14" aria-hidden>
            <rect x="1" y="1" width="12" height="12" rx="3" fill={`color-mix(in srgb, ${conditionStyle[type].fill} 28%, var(--surface))`} stroke={conditionStyle[type].fill} strokeWidth="1.5" />
            {conditionStyle[type].glyph === 'x' && <path d="M3 3 L11 11 M11 3 L3 11" stroke="var(--critical)" strokeWidth="1.5" />}
            {conditionStyle[type].glyph === 'dot' && <circle cx="7" cy="7" r="2.5" fill={conditionStyle[type].fill} />}
          </svg>
          {t(`enum.ToothConditionType.${type}`)}
        </span>
      ))}
      <span className={cn('inline-flex items-center gap-1.5')}>
        <svg width="14" height="14" viewBox="0 0 14 14" aria-hidden><rect x="1" y="1" width="12" height="12" rx="3" fill="none" stroke="var(--text-3)" strokeDasharray="3 2" strokeWidth="1.5" /></svg>
        {t('enum.ConditionStatus.Planned')}
      </span>
    </div>
  )
}
