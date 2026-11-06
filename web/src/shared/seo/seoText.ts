const SITE_NAME = 'UTperfume'

export function formatTitle(title?: string): string {
  return title ? `${title} | ${SITE_NAME}` : `${SITE_NAME} – Perfume catalogue`
}

/** JSON-LD text. `<` is escaped (as the six characters backslash-u-0-0-3-c) so data can never close the script element. */
export function serializeJsonLd(data: object): string {
  return JSON.stringify(data).replace(/</g, '\\u003c')
}
