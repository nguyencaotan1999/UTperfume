import { formatVnd } from '../lib/formatVnd'

interface PriceProps {
  price: number
  /** Compare-at price. Shown struck through only when it is above the price (BR-07). */
  originalPrice?: number | null
  prefix?: string
}

export function Price({ price, originalPrice, prefix }: PriceProps) {
  const showOriginal = originalPrice != null && originalPrice > price
  return (
    <p className="price">
      {prefix && <span className="price__prefix">{prefix} </span>}
      <span className="price__current">{formatVnd(price)}</span>
      {showOriginal && (
        <>
          {' '}
          <s className="price__original">
            <span className="visually-hidden">Was </span>
            {formatVnd(originalPrice)}
          </s>
        </>
      )}
    </p>
  )
}
