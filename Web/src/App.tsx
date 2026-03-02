import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { BrowserRouter, Link, Route, Routes, useLocation } from 'react-router-dom'
import Dashboard from './pages/Dashboard'
import Payments from './pages/Payments'
import TopUp from './pages/TopUp'
import { useAuthStore } from './store/authStore'

const queryClient = new QueryClient({
  defaultOptions: { queries: { staleTime: 10_000 } },
})

function NavLink({ to, children }: { to: string; children: React.ReactNode }) {
  const { pathname } = useLocation()
  const active = pathname === to
  return (
    <Link
      to={to}
      className={`text-sm font-medium px-3 py-2 rounded-lg transition-colors ${
        active ? 'bg-indigo-100 text-indigo-700' : 'text-gray-600 hover:text-gray-900'
      }`}
    >
      {children}
    </Link>
  )
}

function Layout() {
  const { logout, accountId } = useAuthStore()

  return (
    <div className="min-h-screen bg-gray-50">
      <nav className="bg-white border-b border-gray-200 sticky top-0 z-10">
        <div className="max-w-2xl mx-auto px-4 py-3 flex items-center justify-between">
          <span className="font-bold text-indigo-600 text-lg">LicensePay</span>
          <div className="flex items-center gap-1">
            <NavLink to="/">Dashboard</NavLink>
            <NavLink to="/payments">Payments</NavLink>
            <NavLink to="/topup">Top Up</NavLink>
          </div>
          {accountId && (
            <button
              onClick={logout}
              className="text-xs text-gray-400 hover:text-gray-600 ml-2"
            >
              Logout
            </button>
          )}
        </div>
      </nav>
      <main>
        <Routes>
          <Route path="/" element={<Dashboard />} />
          <Route path="/payments" element={<Payments />} />
          <Route path="/topup" element={<TopUp />} />
        </Routes>
      </main>
    </div>
  )
}

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <Layout />
      </BrowserRouter>
    </QueryClientProvider>
  )
}
