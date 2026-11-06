import { useState } from 'react'
import type { ProductImageDto } from '../../api/types'

interface ProductGalleryProps {
  productName: string
  /** Primary image first, then by sort order (the API orders them). */
  images: ProductImageDto[]
}

/** Large image with keyboard-operable thumbnails. Alt text comes from the API, with the product name as a fallback. */
export function ProductGallery({ productName, images }: ProductGalleryProps) {
  const [selected, setSelected] = useState(0)
  const current = images[selected] ?? images[0]

  if (!current) {
    return <div className="gallery__placeholder" role="img" aria-label={`${productName}, no image available`} />
  }

  return (
    <div className="gallery">
      <img className="gallery__main" src={current.url} alt={current.altText ?? productName} />
      {images.length > 1 && (
        <ul className="gallery__thumbs" aria-label="Product images">
          {images.map((image, index) => (
            <li key={image.url}>
              <button
                type="button"
                aria-label={image.altText ?? `${productName}, image ${index + 1}`}
                aria-pressed={index === selected}
                onClick={() => setSelected(index)}
              >
                <img src={image.url} alt="" loading="lazy" />
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
