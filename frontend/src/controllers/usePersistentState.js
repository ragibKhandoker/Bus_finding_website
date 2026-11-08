import { useCallback, useEffect, useState } from 'react'

// State that survives leaving a page and coming back (the menu, a link, or the browser's Back button), so work
// left half done is still there: a search, a form, a draft, a choice. Every page is rebuilt when it opens, so
// plain useState starts empty again; this keeps a copy in sessionStorage, which belongs to this browser tab only
// and is gone when the tab is closed. Signing out clears it all (clearPersistentState).
//
// Use it like useState, with a key naming what is kept, e.g. usePersistentState('map:start', null).
// A null key keeps nothing (plain state). Never use it for passwords, files or anything secret.

export const PERSIST_PREFIX = 'sc_keep:'

const read = (storageKey, initial) => {
  if (storageKey) {
    try {
      const raw = sessionStorage.getItem(storageKey)
      if (raw !== null) return JSON.parse(raw)
    } catch { /* storage blocked or the value unreadable: start fresh */ }
  }
  return typeof initial === 'function' ? initial() : initial
}

export function usePersistentState(key, initial) {
  const storageKey = key ? PERSIST_PREFIX + key : null
  const [state, setState] = useState(() => ({ key: storageKey, value: read(storageKey, initial) }))

  // The key changed (e.g. another chat opened): show what was kept under the new key.
  let current = state
  if (state.key !== storageKey) {
    current = { key: storageKey, value: read(storageKey, initial) }
    setState(current)
  }

  useEffect(() => {
    if (!current.key) return
    try {
      if (current.value === undefined) sessionStorage.removeItem(current.key)
      else sessionStorage.setItem(current.key, JSON.stringify(current.value))
    } catch { /* full or blocked: the state still works, it just is not kept */ }
  }, [current.key, current.value])

  const setValue = useCallback((next) => setState((s) => ({ key: s.key, value: typeof next === 'function' ? next(s.value) : next })), [])
  return [current.value, setValue]
}

// Forget everything kept (on sign-out, so the next person on this tab does not see it).
export function clearPersistentState() {
  try {
    for (let i = sessionStorage.length - 1; i >= 0; i--) {
      const k = sessionStorage.key(i)
      if (k?.startsWith(PERSIST_PREFIX)) sessionStorage.removeItem(k)
    }
  } catch { /* storage blocked */ }
}
