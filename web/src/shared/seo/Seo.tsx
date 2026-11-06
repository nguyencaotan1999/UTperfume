import { formatTitle, serializeJsonLd } from './seoText'

export function JsonLd({ data }: { data: object }) {
  return <script type="application/ld+json" dangerouslySetInnerHTML={{ __html: serializeJsonLd(data) }} />
}

interface SeoProps {
  title?: string
  /** A complete title used as is (for example the API title "Name – Brand | UTperfume"). Wins over `title`. */
  fullTitle?: string
  description?: string
  /** Site-relative canonical path with its query, for example `/perfumes?brand=dior`. */
  canonicalPath?: string
  noindex?: boolean
  /** Structured data objects, each emitted as its own JSON-LD script. */
  jsonLd?: readonly object[]
}

/** Per-page title, description, canonical link and JSON-LD. React 19 hoists these elements into the head. */
export function Seo({ title, fullTitle, description, canonicalPath, noindex, jsonLd }: SeoProps) {
  const canonical = canonicalPath ? new URL(canonicalPath, window.location.origin).toString() : undefined
  return (
    <>
      <title>{fullTitle ?? formatTitle(title)}</title>
      {description && <meta name="description" content={description} />}
      {canonical && <link rel="canonical" href={canonical} />}
      {noindex && <meta name="robots" content="noindex" />}
      {jsonLd?.map((data, index) => <JsonLd key={index} data={data} />)}
    </>
  )
}
