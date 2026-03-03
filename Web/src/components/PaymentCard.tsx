import type { PaymentDto } from '../api/paymentsApi'

const statusColors: Record<PaymentDto['status'], string> = {
  Pending:   'bg-yellow-100 text-yellow-800',
  Paid:      'bg-green-100  text-green-800',
  Overdue:   'bg-red-100    text-red-800',
  Cancelled: 'bg-gray-100   text-gray-500',
  Failed:    'bg-red-100    text-red-700',
}

interface Props {
  payment: PaymentDto
}

export default function PaymentCard({ payment }: Props) {
  return (
    <div className="bg-white border border-gray-200 rounded-xl p-4 flex items-center justify-between shadow-sm hover:shadow-md transition-shadow">
      <div>
        <p className="font-semibold text-gray-800">{payment.type}</p>
        <p className="text-sm text-gray-500 mt-0.5">
          {new Date(payment.createdAt).toLocaleDateString()}
          {payment.paidAt && ` · Paid ${new Date(payment.paidAt).toLocaleDateString()}`}
        </p>
      </div>
      <div className="flex items-center gap-3">
        <span
          className={`text-xs font-medium px-2.5 py-1 rounded-full ${statusColors[payment.status]}`}
        >
          {payment.status}
        </span>
        <span className="text-lg font-bold text-gray-900">{payment.amount.toFixed(2)} GEL</span>
      </div>
    </div>
  )
}
