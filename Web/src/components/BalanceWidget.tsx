import { useQuery } from '@tanstack/react-query'
import { balanceApi } from '../api/paymentsApi'

interface Props {
  accountId: string
}

export default function BalanceWidget({ accountId }: Props) {
  const { data, isLoading, isError } = useQuery({
    queryKey: ['balance', accountId],
    queryFn: () => balanceApi.getBalance(accountId),
    refetchInterval: 30_000,
  })

  if (isLoading)
    return (
      <div className="animate-pulse h-20 bg-gray-100 rounded-xl" />
    )

  if (isError)
    return (
      <div className="bg-red-50 border border-red-200 rounded-xl p-4 text-red-600 text-sm">
        Failed to load balance.
      </div>
    )

  return (
    <div className="bg-gradient-to-br from-indigo-500 to-purple-600 rounded-xl p-6 text-white shadow-lg">
      <p className="text-sm font-medium opacity-80 uppercase tracking-wide">Available Balance</p>
      <p className="text-4xl font-bold mt-1">
        {data?.amount.toFixed(2)} <span className="text-xl font-medium">GEL</span>
      </p>
    </div>
  )
}
