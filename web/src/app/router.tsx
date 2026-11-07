import { lazy, Suspense } from 'react'
import { Route, Routes } from 'react-router'
import { Spinner } from '../shared/ui/Spinner'
import { BrandListPage } from '../features/brands/BrandListPage'
import { BrandPage } from '../features/brands/BrandPage'
import { CataloguePage } from '../features/catalogue/CataloguePage'
import { CategoryPage } from '../features/categories/CategoryPage'
import { HomePage } from '../features/home/HomePage'
import { ProductPage } from '../features/product/ProductPage'
import { PublicLayout } from './layouts/PublicLayout'
import { NotFoundPage } from './NotFoundPage'

// The admin code is a separate chunk, so storefront visitors do not download it (architecture 5).
const AdminRoutes = lazy(() => import('../features/admin/AdminRoutes'))

/** Client routes (architecture 5.1). */
export function AppRoutes() {
  return (
    <Routes>
      <Route
        path="admin/*"
        element={
          <Suspense fallback={<Spinner label="Loading" />}>
            <AdminRoutes />
          </Suspense>
        }
      />
      <Route element={<PublicLayout />}>
        <Route index element={<HomePage />} />
        <Route path="perfumes" element={<CataloguePage />} />
        <Route path="perfumes/:slug" element={<ProductPage />} />
        <Route path="brands" element={<BrandListPage />} />
        <Route path="brands/:slug" element={<BrandPage />} />
        <Route path="categories/:slug" element={<CategoryPage />} />
        <Route path="*" element={<NotFoundPage />} />
      </Route>
    </Routes>
  )
}
