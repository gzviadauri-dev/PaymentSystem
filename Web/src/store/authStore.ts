import { create } from 'zustand'
import { persist } from 'zustand/middleware'

interface AuthState {
  token: string | null
  accountId: string | null
  licenseId: string | null
  setToken: (token: string) => void
  setIds: (accountId: string, licenseId: string) => void
  logout: () => void
}

export const useAuthStore = create<AuthState>()(
  persist(
    (set) => ({
      token: null,
      accountId: null,
      licenseId: null,
      setToken: (token) => set({ token }),
      setIds: (accountId, licenseId) => set({ accountId, licenseId }),
      logout: () => set({ token: null, accountId: null, licenseId: null }),
    }),
    { name: 'auth' }
  )
)
