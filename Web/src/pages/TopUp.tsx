import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { balanceApi } from '../api/paymentsApi'
import { useAuthStore } from '../store/authStore'

const PRESET_AMOUNTS = [50, 100, 200, 500]

export default function TopUp() {
  const { accountId } = useAuthStore()
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  const [amount, setAmount] = useState('')
  const [success, setSuccess] = useState(false)

  const mutation = useMutation({
    mutationFn: () => balanceApi.topUp(accountId!, parseFloat(amount)),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['balance', accountId] })
      setSuccess(true)
      setTimeout(() => navigate('/'), 1500)
    },
  })

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault()
    if (!accountId || !amount || parseFloat(amount) <= 0) return
    mutation.mutate()
  }

  return (
    <div className="max-w-md mx-auto px-4 py-8">
      <h1 className="text-2xl font-bold text-gray-900 mb-6">Top Up Balance</h1>

      {success ? (
        <div className="bg-green-50 border border-green-200 rounded-xl p-6 text-center">
          <p className="text-green-700 font-semibold text-lg">Balance topped up!</p>
          <p className="text-green-600 text-sm mt-1">Redirecting…</p>
        </div>
      ) : (
        <form onSubmit={handleSubmit} className="space-y-6">
          <div className="grid grid-cols-4 gap-2">
            {PRESET_AMOUNTS.map((preset) => (
              <button
                key={preset}
                type="button"
                onClick={() => setAmount(String(preset))}
                className={`py-2 rounded-lg text-sm font-medium border transition-colors ${
                  amount === String(preset)
                    ? 'bg-indigo-600 text-white border-indigo-600'
                    : 'bg-white text-gray-700 border-gray-300 hover:border-indigo-400'
                }`}
              >
                {preset} GEL
              </button>
            ))}
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">
              Custom amount (GEL)
            </label>
            <input
              type="number"
              min="1"
              step="0.01"
              value={amount}
              onChange={(e) => setAmount(e.target.value)}
              placeholder="0.00"
              className="w-full border border-gray-300 rounded-lg px-4 py-2.5 text-gray-900 focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500 outline-none"
            />
          </div>

          {mutation.isError && (
            <p className="text-red-600 text-sm">Failed to top up. Please try again.</p>
          )}

          <button
            type="submit"
            disabled={mutation.isPending || !amount || parseFloat(amount) <= 0}
            className="w-full bg-indigo-600 text-white font-semibold py-3 rounded-lg hover:bg-indigo-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
          >
            {mutation.isPending ? 'Processing…' : 'Top Up'}
          </button>
        </form>
      )}
    </div>
  )
}
