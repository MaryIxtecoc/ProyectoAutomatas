// Reglas de teléfono del lado del cliente. Son las mismas que valida el
// backend (PhonePolicy.cs): solo dígitos, "+" opcional al inicio y espacios
// o guiones como separadores, entre 8 y 15 dígitos.

export const PHONE_PATTERN = '\\+?[0-9]+([ \\-][0-9]+)*'

export const PHONE_ERROR =
  'El teléfono solo puede contener números (8 a 15 dígitos; se permite + al inicio, espacios o guiones).'

/** Quita al vuelo todo lo que no sea número, "+", espacio o guion. */
export function sanitizePhoneInput(value: string): string {
  return (value ?? '')
    .replace(/[^0-9+\s-]/g, '')
    .replace(/(?!^)\+/g, '')
}

/** true si el teléfono cumple el formato y tiene entre 8 y 15 dígitos. */
export function isValidPhone(value: string): boolean {
  const v = (value ?? '').trim().replace(/\s+/g, ' ')
  if (!/^\+?[0-9]+([ -][0-9]+)*$/.test(v)) return false
  const digits = v.replace(/\D/g, '').length
  return digits >= 8 && digits <= 15
}
