import i18n from 'i18next'
import { initReactI18next } from 'react-i18next'
import en from './en'
import ar from './ar'

export type Language = 'en' | 'ar'

const stored = (() => {
  try {
    return localStorage.getItem('tahona.lang') as Language | null
  } catch {
    return null
  }
})()

void i18n.use(initReactI18next).init({
  resources: { en: { translation: en }, ar: { translation: ar } },
  lng: stored ?? 'en',
  fallbackLng: 'en',
  interpolation: { escapeValue: false },
  returnNull: false,
})

export function applyLanguage(lang: Language) {
  document.documentElement.lang = lang
  document.documentElement.dir = lang === 'ar' ? 'rtl' : 'ltr'
  try {
    localStorage.setItem('tahona.lang', lang)
  } catch {
    /* storage unavailable */
  }
  void i18n.changeLanguage(lang)
}

applyLanguage((stored ?? 'en') as Language)

export default i18n
