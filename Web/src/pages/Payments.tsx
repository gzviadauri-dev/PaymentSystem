import { useQuery } from '@tanstack/react-query'
import { paymentsApi } from '../api/paymentsApi'
import PaymentCard from '../components/PaymentCard'
import { useAuthStore } from '../store/authStore'

export default function Payments() {
  const { licenseId } = useAuthStore()

  const { data: payments, isLoading, isError } = useQuery({
    queryKey: ['payments', licenseId],
    queryFn: () => paymentsApi.getPayments(licenseId!),
    enabled: !!licenseId,
  })

  return (
    <div className="max-w-2xl mx-auto px-4 py-8 space-y-6">
      <h1 className="text-2xl font-bold text-gray-900">Payment History</h1>

      {isLoading && (
        <div className="space-y-3">
          {[1, 2, 3, 4, 5].map((i) => (
            <div key={i} className="animate-pulse h-16 bg-gray-100 rounded-xl" />
          ))}
        </div>
      )}

      {isError && (
        <div className="bg-red-50 border border-red-200 rounded-xl p-4 text-red-600 text-sm">
          Failed to load payments.
        </div>
      )}

      {!isLoading && !isError && (
        <div className="space-y-3">
          {payments?.length === 0 ? (
            <p className="text-gray-500 text-sm">No payment history.</p>
          ) : (
            payments?.map((p) => <PaymentCard key={p.id} payment={p} />)
          )}
        </div>
      )}
    </div>
  )
}
