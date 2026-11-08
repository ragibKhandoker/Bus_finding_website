import { afterEach } from 'vitest'
import { clearPersistentState } from './controllers/usePersistentState'

// Pages keep half-done work in sessionStorage (usePersistentState). Each test starts without what an earlier
// test left behind.
afterEach(() => clearPersistentState())
