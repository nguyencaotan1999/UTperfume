/** Intl puts a no-break space (code 160) between a number and a currency symbol; tests compare with a normal space. */
export const plain = (text: string | null | undefined): string =>
  (text ?? '').split(String.fromCharCode(160)).join(' ')
