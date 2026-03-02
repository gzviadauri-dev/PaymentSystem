import { useCallback, useRef } from 'react'
import { useMutation, useQueryClient, type UseMutationOptions } from '@tanstack/react-query'
import axios, { type AxiosError } from 'axios'

interface IdempotentMutationOptions<TData, TVariables> {
  /**
   * The async function that performs the request. Must accept an idempotency key
   * as its second argument so the hook can inject the stable per-intent key.
   */
  mutationFn: (variables: TVariables, idempotencyKey: string) => Promise<TData>

  /** React Query cache keys to invalidate on success. */
  invalidateKeys?: readonly unknown[][]

  /**
   * Maximum time (ms) to keep retrying 409 "Processing" responses before giving up.
   * Default: 30 000 (30 seconds).
   */
  processingTimeoutMs?: number

  /** Forwarded to useMutation for additional lifecycle hooks. */
  options?: Omit<UseMutationOptions<TData, Error, TVariables>, 'mutationFn'>
}

/**
 * Wraps a mutating API call with correct idempotency key handling:
 *
 *  1. A UUID is generated on the FIRST call and cached in a ref for the lifetime of
 *     the component. It is NOT regenerated on retry — the same key is always sent.
 *  2. The key is cleared only after a successful non-409 response so that a fresh
 *     operation (e.g. the user taps "Pay" again after success) gets a new key.
 *  3. HTTP 409 responses (server slot is "Processing") are retried automatically
 *     after the Retry-After header value (default 2 s) up to `processingTimeoutMs`.
 *  4. On success, all `invalidateKeys` are invalidated so lists and balances refresh.
 */
export function useIdempotentMutation<TData, TVariables>({
  mutationFn,
  invalidateKeys = [],
  processingTimeoutMs = 30_000,
  options = {},
}: IdempotentMutationOptions<TData, TVariables>) {
  const queryClient = useQueryClient()

  // Stable idempotency key — persists across retries but resets after success.
  const idempotencyKeyRef = useRef<string | null>(null)

  const getOrCreateKey = useCallback((): string => {
    if (!idempotencyKeyRef.current) {
      idempotencyKeyRef.current = crypto.randomUUID()
    }
    return idempotencyKeyRef.current
  }, [])

  const executeWithRetry = useCallback(
    async (variables: TVariables): Promise<TData> => {
      const key = getOrCreateKey()
      const deadline = Date.now() + processingTimeoutMs

      while (true) {
        try {
          return await mutationFn(variables, key)
        } catch (err) {
          if (!axios.isAxiosError(err)) throw err

          const axiosErr = err as AxiosError<{ error?: string }>

          if (axiosErr.response?.status === 409) {
            // Server is still processing a previous request with this key
            if (Date.now() >= deadline) {
              throw new Error(
                'Payment processing timed out. Please check your payment history ' +
                'before trying again to avoid duplicate charges.',
              )
            }

            const retryAfterRaw = axiosErr.response.headers['retry-after']
            const retryAfterMs = retryAfterRaw
              ? parseFloat(String(retryAfterRaw)) * 1000
              : 2000

            await new Promise((resolve) => setTimeout(resolve, retryAfterMs))
            continue
          }

          throw err
        }
      }
    },
    [mutationFn, getOrCreateKey, processingTimeoutMs],
  )

  const mutation = useMutation<TData, Error, TVariables>({
    ...options,
    mutationFn: executeWithRetry,
    // React Query v5 onSuccess receives 4 args: data, variables, onMutateResult, context
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    onSuccess: async (data: TData, variables: TVariables, ...rest: any[]) => {
      // Clear the key so the next user action gets a fresh one
      idempotencyKeyRef.current = null

      // Invalidate all specified query keys so UI reflects the new state
      await Promise.all(
        invalidateKeys.map((key) => queryClient.invalidateQueries({ queryKey: key })),
      )

      // Forward all arguments to the caller's onSuccess in case they use onMutateResult / context
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      ;(options.onSuccess as any)?.(data, variables, ...rest)
    },
    onError: options.onError,
  })

  return {
    ...mutation,
    /**
     * The current idempotency key. Useful for debugging or displaying a
     * transaction reference in the UI while the request is in-flight.
     */
    currentKey: idempotencyKeyRef.current,
  }
}
