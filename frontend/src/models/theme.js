// Colour theme: 'light', 'dark' or 'night', set as data-theme on <html> (the colours are in theme.css).
// index.html applies the saved choice before the page is drawn, so the page never flashes the wrong theme.
export const THEMES = ['light', 'dark', 'night']
const STORAGE_KEY = 'sc_theme'

export const getTheme = () => THEMES.includes(document.documentElement.dataset.theme) ? document.documentElement.dataset.theme : 'light'

export function setTheme(theme) {
  if (!THEMES.includes(theme)) return
  document.documentElement.dataset.theme = theme
  try { localStorage.setItem(STORAGE_KEY, theme) } catch { /* the choice just is not remembered */ }
  syncThemeColor()
}

// The browser's own UI, such as the address bar on phones, follows the page background.
export function syncThemeColor() {
  const meta = document.querySelector('meta[name="theme-color"]')
  if (meta) meta.content = getComputedStyle(document.documentElement).backgroundColor
}
