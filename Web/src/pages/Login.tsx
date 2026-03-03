import { useMutation } from '@tanstack/react-query'
import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { authApi } from '../api/paymentsApi'
import { useAuthStore } from '../store/authStore'

export default function Login() {
  const { setToken, setIds } = useAuthStore()
  const navigate = useNavigate()
  const [licenseId, setLicenseId] = useState('')

  const mutation = useMutation({
    mutationFn: () => authApi.login(licenseId.trim()),
    onSuccess: (data) => {
      setToken(data.token)
      setIds(data.accountId, data.licenseId)
      navigate('/')
    },
  })

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault()
    if (!licenseId.trim()) return
    mutation.mutate()
  }

  return (
    <div className="min-h-screen bg-gray-50 flex items-center justify-center px-4">
      <div className="w-full max-w-sm bg-white rounded-2xl shadow-sm border border-gray-200 p-8">
        <div className="mb-8 text-center">
          <span className="text-2xl font-bold text-indigo-600">LicensePay</span>
          <p className="text-gray-500 text-sm mt-1">Enter your License ID to continue</p>
        </div>

        <form onSubmit={handleSubmit} className="space-y-4">
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">
              License ID
            </label>
            <input
              type="text"
              value={licenseId}
              onChange={(e) => setLicenseId(e.target.value)}
              placeholder="xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
              className="w-full border border-gray-300 rounded-lg px-4 py-2.5 text-sm text-gray-900 focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500 outline-none font-mono"
              autoFocus
            />
          </div>

          {mutation.isError && (
            <p className="text-red-600 text-sm">
              {(mutation.error as { response?: { data?: { error?: string } } })?.response?.data?.error
                ?? 'Login failed. Check your License ID.'}
            </p>
          )}

          <button
            type="submit"
            disabled={mutation.isPending || !licenseId.trim()}
            className="w-full bg-indigo-600 text-white font-semibold py-2.5 rounded-lg hover:bg-indigo-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
          >
            {mutation.isPending ? 'Signing in…' : 'Sign In'}
          </button>
        </form>
      </div>
    </div>
  )
}
