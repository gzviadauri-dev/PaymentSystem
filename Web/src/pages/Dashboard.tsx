import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { paymentsApi } from '../api/paymentsApi'
import BalanceWidget from '../components/BalanceWidget'
import PaymentCard from '../components/PaymentCard'
import { useAuthStore } from '../store/authStore'

export default function Dashboard() {
  const { accountId, licenseId } = useAuthStore()

  const { data: payments, isLoading } = useQuery({
    queryKey: ['payments', licenseId],
    queryFn: () => paymentsApi.getPayments(licenseId!),
    enabled: !!licenseId,
  })

  const pending = payments?.filter((p) => p.status === 'Pending') ?? []
  const recent = payments?.slice(0, 5) ?? []

  return (
    <div className="max-w-2xl mx-auto px-4 py-8 space-y-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-bold text-gray-900">Dashboard</h1>
        <Link
          to="/topup"
          className="bg-indigo-600 text-white text-sm font-medium px-4 py-2 rounded-lg hover:bg-indigo-700 transition-colors"
        >
          Top Up
        </Link>
      </div>

      {accountId && <BalanceWidget accountId={accountId} />}

      {pending.length > 0 && (
        <div className="bg-amber-50 border border-amber-200 rounded-xl p-4">
          <p className="text-amber-800 font-medium text-sm">
            {pending.length} pending payment{pending.length > 1 ? 's' : ''} — total{' '}
            <strong>
              {pending.reduce((sum, p) => sum + p.amount, 0).toFixed(2)} GEL
            </strong>
          </p>
        </div>
      )}

      <div>
        <div className="flex items-center justify-between mb-3">
          <h2 className="text-lg font-semibold text-gray-800">Recent Payments</h2>
          <Link to="/payments" className="text-sm text-indigo-600 hover:underline">
            View all
          </Link>
        </div>

        {isLoading ? (
          <div className="space-y-3">
            {[1, 2, 3].map((i) => (
              <div key={i} className="animate-pulse h-16 bg-gray-100 rounded-xl" />
            ))}
          </div>
        ) : recent.length === 0 ? (
          <p className="text-gray-500 text-sm">No payments yet.</p>
        ) : (
          <div className="space-y-3">
            {recent.map((p) => (
              <PaymentCard key={p.id} payment={p} />
            ))}
          </div>
        )}
      </div>
    </div>
  )
}
