import React from 'react'
import { CircleAlert, House, RefreshCw } from 'lucide-react'
import { t } from '../../models/i18n'

// Catches a page that fails while drawing, so the visitor sees a message with a way out instead of a blank
// white screen (React removes everything on screen when a render error is not caught). The header and footer
// stay usable; App gives it a key per page, so moving to another page starts fresh.
export class PageErrorBoundary extends React.Component {
  constructor(props) {
    super(props)
    this.state = { error: null }
  }

  static getDerivedStateFromError(error) {
    return { error }
  }

  componentDidCatch(error, info) {
    console.error('A page failed to draw:', error, info?.componentStack)
  }

  render() {
    if (!this.state.error) return this.props.children
    return <div className="page inner-page">
      <div className="dv-panel dv-empty" role="alert">
        <span><CircleAlert size={24} /></span>
        <b>{t('This page could not be shown')}</b>
        <small>{t('Something went wrong while opening this page. Reload it, or go back to the home page.')}</small>
        {/* The technical reason, so it can be reported (it never contains the visitor's data, only code names). */}
        <details className="dv-small" style={{ maxWidth: '100%', overflowWrap: 'anywhere' }}>
          <summary>{t('Technical details')}</summary>
          <code>{String(this.state.error?.message || this.state.error)}</code>
        </details>
        <div className="dv-row">
          <button type="button" className="button button-green" onClick={() => this.setState({ error: null })}><RefreshCw size={15} /> {t('Try again')}</button>
          <button type="button" className="button button-outline" onClick={() => window.location.reload()}><RefreshCw size={15} /> {t('Reload page')}</button>
          <button type="button" className="button button-outline" onClick={() => this.props.onHome?.()}><House size={15} /> {t('Go to home')}</button>
        </div>
      </div>
    </div>
  }
}
