import { Route, Routes } from 'react-router'
import { AdminHomePage } from './AdminHomePage'
import { AdminLayout } from './AdminLayout'
import { AdminLoginPage } from './AdminLoginPage'
import { AdminNotFoundPage } from './AdminNotFoundPage'

/** The admin route tree under `/admin/*`. It is loaded lazily as one chunk (architecture 5). */
export default function AdminRoutes() {
  return (
    <Routes>
      <Route path="login" element={<AdminLoginPage />} />
      <Route element={<AdminLayout />}>
        <Route index element={<AdminHomePage />} />
        <Route path="*" element={<AdminNotFoundPage />} />
      </Route>
    </Routes>
  )
}
