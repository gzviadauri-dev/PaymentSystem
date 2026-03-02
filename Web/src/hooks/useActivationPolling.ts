import { useEffect, useRef, useState } from 'react'

export type ActivationStatus =
  | 'idle'
  | 'polling'
  | 'activated'
  | 'timeout'

interface UseActivationPollingOptions {
  /**
   * Async function that fetches the current entity state.
   * Should resolve to an object with an `IsActive` boolean.
   */
  fetchFn: () => Promise<{ IsActive: boolean }>

  /**
   * Whether to start polling. Set to true immediately after a successful
   * payment submission to begin watching for the async activation event.
   */
  enabled: boolean

  /**
   * Interval in milliseconds between poll attempts. Default: 5 000 (5 s).
   */
  intervalMs?: number

  /**
   * Maximum time in milliseconds to poll before giving up. Default: 60 000 (60 s).
   */
  timeoutMs?: number
}

interface UseActivationPollingResult {
  /** Current polling lifecycle state. */
  status: ActivationStatus

  /**
   * Call this to reset the hook back to 'idle' — e.g. when the user navigates
   * away or starts a new payment flow.
   */
  reset: () => void
}

/**
 * Polls `fetchFn` every `intervalMs` until `IsActive` becomes true or
 * `timeoutMs` elapses. Used to detect async business-side activation
 * (vehicle/driver/license activation) after a payment is confirmed.
 *
 * Usage:
 *   const { status } = useActivationPolling({
 *     fetchFn: () => api.getLicense(licenseId),
 *     enabled: paymentSucceeded,
 *   })
 *
 * UI mapping:
 *   'idle'      → nothing shown (pre-payment)
 *   'polling'   → "Payment received — activation pending"
 *   'activated' → "Activated" (stop polling)
 *   'timeout'   → "Activation is taking longer than expected — please refresh or contact support."
 */
export function useActivationPolling({
  fetchFn,
  enabled,
  intervalMs = 5_000,
  timeoutMs = 60_000,
}: UseActivationPollingOptions): UseActivationPollingResult {
  const [status, setStatus] = useState<ActivationStatus>('idle')
  const deadlineRef = useRef<number | null>(null)
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null)

  const clearTimer = () => {
    if (timerRef.current !== null) {
      clearTimeout(timerRef.current)
      timerRef.current = null
    }
  }

  const reset = () => {
    clearTimer()
    deadlineRef.current = null
    setStatus('idle')
  }

  useEffect(() => {
    if (!enabled) return

    setStatus('polling')
    deadlineRef.current = Date.now() + timeoutMs

    const poll = async () => {
      if (deadlineRef.current !== null && Date.now() >= deadlineRef.current) {
        setStatus('timeout')
        return
      }

      try {
        const entity = await fetchFn()
        if (entity.IsActive) {
          setStatus('activated')
          return
        }
      } catch {
        // Transient fetch errors are silently swallowed; polling continues
        // until the timeout. Callers can observe 'timeout' to surface issues.
      }

      timerRef.current = setTimeout(poll, intervalMs)
    }

    // Start the first poll immediately
    void poll()

    return () => {
      clearTimer()
    }
  }, [enabled, fetchFn, intervalMs, timeoutMs])

  return { status, reset }
}
