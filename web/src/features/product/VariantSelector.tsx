import { useId, useState } from 'react'
import type { VariantDto } from '../../api/types'
import { formatVnd } from '../../shared/lib/formatVnd'
import { AvailabilityBadge } from '../../shared/ui/AvailabilityBadge'
import { Price } from '../../shared/ui/Price'

/** Starts on the first variant that can be bought, otherwise on the first one. */
function defaultVariant(variants: readonly VariantDto[]): string | undefined {
  return (variants.find((v) => v.availability !== 'OutOfStock') ?? variants[0])?.sku
}

/**
 * Variant (size) selection as a native radio group, so it is labelled and keyboard operable (arrow keys). Each
 * option shows volume, price and its availability status; the selected variant's price, compare-at price, discount
 * and status are repeated below. A quantity is never shown.
 */
export function VariantSelector({ variants }: { variants: VariantDto[] }) {
  const name = useId()
  const [sku, setSku] = useState(() => defaultVariant(variants))
  const selected = variants.find((v) => v.sku === sku) ?? variants[0]

  if (!selected) return <p className="empty">No sizes are available for this perfume.</p>

  return (
    <div className="variants">
      <fieldset className="variants__group">
        <legend>Size</legend>
        {variants.map((variant) => {
          const id = `${name}-${variant.sku}`
          return (
            <div key={variant.sku} className="variants__option">
              <input
                id={id}
                type="radio"
                name={name}
                value={variant.sku}
                checked={variant.sku === selected.sku}
                onChange={() => setSku(variant.sku)}
              />
              <label htmlFor={id}>
                <span className="variants__volume">{variant.volumeMl} ml</span>
                <span className="variants__price">{formatVnd(variant.price)}</span>
                <AvailabilityBadge status={variant.availability} />
              </label>
            </div>
          )
        })}
      </fieldset>

      <div className="variants__selected" aria-live="polite">
        <p className="variants__summary">
          {selected.volumeMl} ml <span className="variants__sku">SKU {selected.sku}</span>
        </p>
        <Price price={selected.price} originalPrice={selected.originalPrice} />
        {selected.discountPercent != null && selected.originalPrice != null && selected.originalPrice > selected.price && (
          <p className="variants__discount">Save {selected.discountPercent}%</p>
        )}
        <AvailabilityBadge status={selected.availability} />
      </div>
    </div>
  )
}
