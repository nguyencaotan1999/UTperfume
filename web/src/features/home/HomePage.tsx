import { Link } from 'react-router'
import { useHome } from '../../api/queries/home'
import type { BannerDto, BrandSummary, CategorySummary, ProductSummary } from '../../api/types'
import { perfumeCount } from '../../shared/lib/plural'
import { routes, safeInternalPath } from '../../shared/lib/routes'
import { Seo } from '../../shared/seo/Seo'
import { ProductCard } from '../../shared/ui/ProductCard'
import { Spinner } from '../../shared/ui/Spinner'

/** Home (E1). Every section has its own empty state, and the page heading shows whatever the data does. */
export function HomePage() {
  const home = useHome()

  return (
    <div className="page home">
      <Seo
        description="Discover perfumes by brand, category and concentration. Prices in Vietnamese dong."
        canonicalPath="/"
      />

      <section className="hero" aria-labelledby="home-title">
        <h1 id="home-title">Find your signature fragrance</h1>
        <p>Browse perfumes from leading houses, with clear prices and availability.</p>
        <Link to={routes.catalogue} className="button">
          Browse all perfumes
        </Link>
      </section>

      {home.isPending && <Spinner label="Loading the home page" />}

      {home.isError && (
        <div role="alert" className="notice notice--error">
          <p>The home page content could not be loaded.</p>
          <button type="button" className="button" onClick={() => void home.refetch()}>
            Try again
          </button>
        </div>
      )}

      {home.data && (
        <>
          <Banners banners={home.data.banners} />
          <ProductSection
            id="featured"
            title="Featured perfumes"
            products={home.data.featuredProducts}
            empty="No featured perfumes yet."
          />
          <ProductSection
            id="new-arrivals"
            title="New arrivals"
            products={home.data.newArrivals}
            empty="No new arrivals yet."
          />
          <ProductSection
            id="best-sellers"
            title="Best sellers"
            products={home.data.bestSellers}
            empty="No best sellers yet."
          />
          <PopularBrands brands={home.data.popularBrands} />
          <Categories categories={home.data.categories} />
        </>
      )}
    </div>
  )
}

function Banners({ banners }: { banners: BannerDto[] }) {
  return (
    <section aria-labelledby="banners-title" className="banners">
      <h2 id="banners-title">Highlights</h2>
      {banners.length === 0 ? (
        <p className="empty">No current promotions.</p>
      ) : (
        <ul className="banners__list">
          {banners.map((banner) => {
            const link = safeInternalPath(banner.linkUrl)
            const content = (
              <>
                <img src={banner.imageUrl} alt={banner.altText} loading="lazy" />
                <span className="banners__text">
                  <strong>{banner.title}</strong>
                  {banner.subtitle && <span>{banner.subtitle}</span>}
                </span>
              </>
            )
            return (
              <li key={`${banner.title}-${banner.imageUrl}`}>
                {link ? <Link to={link}>{content}</Link> : <div>{content}</div>}
              </li>
            )
          })}
        </ul>
      )}
    </section>
  )
}

interface ProductSectionProps {
  id: string
  title: string
  products: ProductSummary[]
  empty: string
}

function ProductSection({ id, title, products, empty }: ProductSectionProps) {
  const headingId = `${id}-title`
  return (
    <section aria-labelledby={headingId} className="section">
      <h2 id={headingId}>{title}</h2>
      {products.length === 0 ? (
        <p className="empty">{empty}</p>
      ) : (
        <ul className="grid">
          {products.map((product) => (
            <li key={product.id}>
              <ProductCard product={product} />
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}

function PopularBrands({ brands }: { brands: BrandSummary[] }) {
  return (
    <section aria-labelledby="brands-title" className="section">
      <h2 id="brands-title">Popular brands</h2>
      {brands.length === 0 ? (
        <p className="empty">No popular brands yet.</p>
      ) : (
        <ul className="tiles">
          {brands.map((brand) => (
            <li key={brand.slug}>
              <Link to={routes.brand(brand.slug)}>
                {brand.logoUrl && <img src={brand.logoUrl} alt="" loading="lazy" />}
                <span>{brand.name}</span>
                <span className="tiles__count">{perfumeCount(brand.productCount)}</span>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}

function Categories({ categories }: { categories: CategorySummary[] }) {
  return (
    <section aria-labelledby="categories-title" className="section">
      <h2 id="categories-title">Shop by category</h2>
      {categories.length === 0 ? (
        <p className="empty">No categories yet.</p>
      ) : (
        <ul className="tiles">
          {categories.map((category) => (
            <li key={category.slug}>
              <Link to={routes.category(category.slug)}>
                {category.imageUrl && <img src={category.imageUrl} alt="" loading="lazy" />}
                <span>{category.name}</span>
                <span className="tiles__count">{perfumeCount(category.productCount)}</span>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}
