import { useQueryClient } from '@tanstack/react-query'
import { useCallback, useState } from 'react'
import { generateAndPay } from '../api/paymentsApi'
import { useAuthStore } from '../store/authStore'
import { toast } from '../store/toastStore'

interface PaymentConfig {
  type: string
  label: string
  amount: number
  icon: string
}

const PAYMENT_TYPES: PaymentConfig[] = [
  { type: 'Monthly',       label: 'Monthly Fee',      amount: 150,  icon: '📅' },
  { type: 'AddVehicle',    label: 'Add Vehicle',       amount: 200,  icon: '🚗' },
  { type: 'AddDriver',     label: 'Add Driver',        amount: 100,  icon: '👤' },
  { type: 'LicenseSell',   label: 'License Sale',      amount: 500,  icon: '📄' },
  { type: 'LicenceCancel', label: 'Cancel License',    amount: 50,   icon: '❌' },
]

type BtnState = 'idle' | 'pending' | 'success' | 'error'

function statusClass(s: BtnState) {
  if (s === 'pending') return 'bg-gray-100 text-gray-400 cursor-not-allowed'
  if (s === 'success') return 'bg-green-50 border-green-300 text-green-700'
  if (s === 'error')   return 'bg-red-50 border-red-300 text-red-700'
  return 'bg-white hover:border-indigo-400 hover:shadow-sm text-gray-800 cursor-pointer'
}

export default function QuickPay() {
  const { licenseId, accountId } = useAuthStore()
  const queryClient = useQueryClient()

  const [btnStates, setBtnStates] = useState<Record<string, BtnState>>({})
  const [stressState, setStressState] = useState<BtnState>('idle')
  const [stressResults, setStressResults] = useState<string | null>(null)

  const invalidate = useCallback(() => {
    queryClient.invalidateQueries({ queryKey: ['payments', licenseId] })
    queryClient.invalidateQueries({ queryKey: ['balance', accountId] })
  }, [queryClient, licenseId, accountId])

  const handlePay = useCallback(async (cfg: PaymentConfig) => {
    if (!licenseId || !accountId) return
    if (btnStates[cfg.type] === 'pending') return

    setBtnStates((s) => ({ ...s, [cfg.type]: 'pending' }))
    try {
      const result = await generateAndPay(licenseId, accountId, cfg.type, cfg.amount)
      if (result.success) {
        setBtnStates((s) => ({ ...s, [cfg.type]: 'success' }))
        toast('success', `${cfg.label} paid`, `${cfg.amount} GEL deducted from your balance.`)
        invalidate()
      } else {
        setBtnStates((s) => ({ ...s, [cfg.type]: 'error' }))
        toast('error', result.reason ?? 'Payment failed', `Could not complete ${cfg.label}.`)
      }
    } catch (err: unknown) {
      setBtnStates((s) => ({ ...s, [cfg.type]: 'error' }))
      const msg =
        (err as { response?: { data?: { error?: string } } })?.response?.data?.error
        ?? 'Unexpected error. Please try again.'
      toast('error', 'Payment failed', msg)
    } finally {
      setTimeout(() => setBtnStates((s) => ({ ...s, [cfg.type]: 'idle' })), 2500)
    }
  }, [licenseId, accountId, btnStates, invalidate])

  const handleStressTest = useCallback(async () => {
    if (!licenseId || !accountId) return
    if (stressState === 'pending') return

    setStressState('pending')
    setStressResults(null)

    // Fire Monthly, AddVehicle, AddDriver concurrently with independent key pairs
    const targets = PAYMENT_TYPES.slice(0, 3)
    const results = await Promise.allSettled(
      targets.map((cfg) => generateAndPay(licenseId, accountId, cfg.type, cfg.amount)),
    )

    const ok = results.filter((r) => r.status === 'fulfilled' && r.value.success).length
    const failed = results.length - ok

    setStressState(failed === 0 ? 'success' : ok > 0 ? 'success' : 'error')
    setStressResults(`${ok}/${results.length} paid`)
    invalidate()

    if (ok > 0)
      toast('success', `Stress test: ${ok}/${results.length} paid`, `${ok * 150} GEL approximate deduction.`)
    if (failed > 0) {
      const reasons = results
        .filter((r): r is PromiseFulfilledResult<{ success: boolean; reason?: string }> =>
          r.status === 'fulfilled' && !r.value.success)
        .map((r) => r.value.reason)
        .filter(Boolean)
      toast('error', `${failed} payment(s) failed`, reasons[0] ?? 'Check your balance.')
    }

    setTimeout(() => {
      setStressState('idle')
      setStressResults(null)
    }, 3000)
  }, [licenseId, accountId, stressState, invalidate])

  return (
    <div className="bg-white border border-gray-200 rounded-2xl p-5 shadow-sm">
      <h2 className="text-base font-semibold text-gray-800 mb-4">Quick Pay</h2>

      <div className="grid grid-cols-1 gap-2">
        {PAYMENT_TYPES.map((cfg) => {
          const state = btnStates[cfg.type] ?? 'idle'
          return (
            <button
              key={cfg.type}
              onClick={() => handlePay(cfg)}
              disabled={state === 'pending'}
              className={`flex items-center justify-between px-4 py-3 rounded-xl border transition-all ${statusClass(state)}`}
            >
              <div className="flex items-center gap-3">
                <span className="text-xl">{cfg.icon}</span>
                <div className="text-left">
                  <p className="text-sm font-medium leading-tight">{cfg.label}</p>
                  <p className="text-xs text-gray-400 mt-0.5">{cfg.type}</p>
                </div>
              </div>
              <div className="flex items-center gap-2">
                <span className="text-sm font-semibold">{cfg.amount} GEL</span>
                {state === 'pending' && (
                  <svg className="animate-spin h-4 w-4 text-indigo-500" viewBox="0 0 24 24" fill="none">
                    <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                    <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v8H4z" />
                  </svg>
                )}
                {state === 'success' && <span className="text-green-600 text-sm font-bold">✓</span>}
                {state === 'error'   && <span className="text-red-500  text-sm font-bold">✗</span>}
                {state === 'idle'    && (
                  <span className="text-xs text-indigo-600 font-medium bg-indigo-50 px-2 py-0.5 rounded-full">
                    Pay
                  </span>
                )}
              </div>
            </button>
          )
        })}
      </div>

      {/* Stress test */}
      <div className="mt-4 pt-4 border-t border-gray-100">
        <button
          onClick={handleStressTest}
          disabled={stressState === 'pending'}
          className={`w-full flex items-center justify-center gap-2 py-3 rounded-xl font-semibold text-sm transition-all border ${
            stressState === 'pending'
              ? 'bg-gray-100 text-gray-400 cursor-not-allowed border-gray-200'
              : stressState === 'success'
              ? 'bg-green-50 border-green-300 text-green-700'
              : stressState === 'error'
              ? 'bg-red-50 border-red-300 text-red-700'
              : 'bg-indigo-600 border-indigo-600 text-white hover:bg-indigo-700'
          }`}
        >
          {stressState === 'pending' && (
            <svg className="animate-spin h-4 w-4" viewBox="0 0 24 24" fill="none">
              <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
              <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v8H4z" />
            </svg>
          )}
          {stressState === 'pending'
            ? 'Running…'
            : stressResults
            ? `Stress Test — ${stressResults}`
            : '⚡ Stress Test 3×'}
        </button>
        {stressState === 'idle' && !stressResults && (
          <p className="text-xs text-gray-400 text-center mt-1.5">
            Fires Monthly + AddVehicle + AddDriver concurrently
          </p>
        )}
      </div>
    </div>
  )
}
