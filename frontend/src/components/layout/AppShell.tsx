import { useEffect, useState } from 'react'
import { Outlet, useLocation } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { Languages, LogOut, Menu as MenuIcon, Moon, Search, Sun } from 'lucide-react'
import { useAuth } from '@/auth/AuthProvider'
import { applyLanguage } from '@/i18n'
import { useTheme } from '@/lib/theme'
import { displayName } from '@/lib/utils'
import { Button } from '@/components/ui/Button'
import { Avatar } from '@/components/ui/primitives'
import { Menu, MenuItem } from '@/components/ui/Menu'
import { Sidebar } from './Sidebar'
import { CommandPalette } from './CommandPalette'
import { NotificationBell } from './NotificationBell'
import { AccountBanner } from './AccountBanner'

export function AppShell() {
  const { t, i18n } = useTranslation()
  const { user, logout } = useAuth()
  const { theme, toggle } = useTheme()
  const [paletteOpen, setPaletteOpen] = useState(false)
  const [mobileNav, setMobileNav] = useState(false)
  const location = useLocation()

  useEffect(() => { setMobileNav(false) }, [location.pathname])

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === 'k') {
        e.preventDefault()
        setPaletteOpen(true)
      }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [])

  const otherLang = i18n.language === 'ar' ? 'en' : 'ar'

  return (
    <div className="flex h-full">
      <div className="hidden lg:block">
        <Sidebar />
      </div>
      {mobileNav && (
        <div className="fixed inset-0 z-40 lg:hidden">
          <div className="absolute inset-0 bg-black/40" onClick={() => setMobileNav(false)} />
          <div className="absolute inset-y-0 start-0 shadow-pop">
            <Sidebar onNavigate={() => setMobileNav(false)} />
          </div>
        </div>
      )}

      <div className="flex min-w-0 flex-1 flex-col">
        <header className="sticky top-0 z-30 flex h-16 items-center gap-3 border-b border-line bg-surface/85 px-4 backdrop-blur sm:px-6">
          <Button variant="ghost" size="icon" className="lg:hidden" onClick={() => setMobileNav(true)} aria-label="Menu">
            <MenuIcon className="size-5" />
          </Button>

          <button
            onClick={() => setPaletteOpen(true)}
            className="flex h-9 w-full min-w-0 max-w-md items-center gap-2 rounded-lg border border-line bg-surface-2 px-3 text-sm text-ink-3 transition-colors hover:border-line-strong"
          >
            <Search className="size-4" />
            <span className="flex-1 truncate text-start">{t('common.searchPlaceholder')}</span>
            <kbd className="hidden rounded border border-line bg-surface px-1.5 text-[10px] sm:inline">Ctrl K</kbd>
          </button>

          <div className="ms-auto flex items-center gap-1">
            <Button variant="ghost" size="sm" onClick={() => applyLanguage(otherLang)} aria-label={t('common.language')} icon={<Languages className="size-4" />}>
              <span className="hidden sm:inline">{otherLang === 'ar' ? 'العربية' : 'English'}</span>
            </Button>
            <Button variant="ghost" size="icon" onClick={toggle} aria-label={t('common.theme')}>
              {theme === 'dark' ? <Sun className="size-4" /> : <Moon className="size-4" />}
            </Button>
            <NotificationBell />
            <Menu
              trigger={({ toggle: open }) => (
                <button onClick={open} className="ms-1 flex items-center gap-2 rounded-lg p-1 hover:bg-surface-3" aria-label={t('common.profile')}>
                  <Avatar name={user?.fullName ?? '?'} size="sm" />
                  <span className="hidden text-start leading-tight md:block">
                    <span className="block text-xs font-medium text-ink">{user ? displayName(user.fullName, user.fullNameAr) : ''}</span>
                    <span className="block text-[11px] text-ink-3">{user?.role}</span>
                  </span>
                </button>
              )}
            >
              {(close) => (
                <>
                  <div className="border-b border-line px-3 py-2">
                    <p className="text-sm font-medium text-ink">{user?.fullName}</p>
                    <p className="text-xs text-ink-3">{user?.email}</p>
                  </div>
                  <MenuItem icon={<LogOut />} onClick={() => { close(); void logout() }}>{t('common.signOut')}</MenuItem>
                </>
              )}
            </Menu>
          </div>
        </header>

        <main className="flex-1 overflow-y-auto">
          <div className="mx-auto w-full max-w-[1440px] px-4 py-6 sm:px-6 lg:px-8">
            <AccountBanner />
            <Outlet />
          </div>
        </main>
      </div>

      <CommandPalette open={paletteOpen} onClose={() => setPaletteOpen(false)} />
    </div>
  )
}
