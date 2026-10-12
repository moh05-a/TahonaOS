import { useEffect, useState } from 'react'

export type Theme = 'light' | 'dark'

const read = (): Theme => (document.documentElement.classList.contains('dark') ? 'dark' : 'light')

/** Light is primary; dark is an explicit choice persisted per browser. */
export function useTheme() {
  const [theme, setTheme] = useState<Theme>(read)

  useEffect(() => {
    document.documentElement.classList.toggle('dark', theme === 'dark')
    try {
      localStorage.setItem('tahona.theme', theme)
    } catch {
      /* storage unavailable */
    }
  }, [theme])

  return { theme, toggle: () => setTheme((t) => (t === 'dark' ? 'light' : 'dark')) }
}

/** Reads a CSS custom property (for chart colours that must follow the theme). */
export function cssVar(name: string) {
  return getComputedStyle(document.documentElement).getPropertyValue(name).trim()
}

/** Re-renders when the theme class changes, so charts can re-read CSS variables. */
export function useThemeVersion() {
  const [version, setVersion] = useState(0)
  useEffect(() => {
    const obs = new MutationObserver(() => setVersion((v) => v + 1))
    obs.observe(document.documentElement, { attributes: true, attributeFilter: ['class'] })
    return () => obs.disconnect()
  }, [])
  return version
}
