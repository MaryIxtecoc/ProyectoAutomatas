// Filtros de foto compartidos por el registro y el editor del perfil.
// "css" es un filtro de canvas/CSS (ctx.filter).
export const photoFilters = [
  { id: 'none', label: 'Original', css: 'none' },
  { id: 'gray', label: 'Blanco y negro', css: 'grayscale(1)' },
  {
    id: 'grayHi',
    label: 'B/N contraste',
    css: 'grayscale(1) contrast(1.3)'
  },
  {
    id: 'sepia',
    label: 'Sepia',
    css: 'sepia(0.85) saturate(1.3)'
  },
  {
    id: 'vivid',
    label: 'Vívido',
    css: 'saturate(1.7) contrast(1.15)'
  },
  {
    id: 'cool',
    label: 'Frío',
    css: 'hue-rotate(180deg) saturate(1.2)'
  },
  {
    id: 'warm',
    label: 'Cálido',
    css: 'sepia(0.3) saturate(1.4) hue-rotate(-10deg)'
  },
  {
    id: 'vintage',
    label: 'Vintage',
    css: 'sepia(0.5) contrast(0.9) brightness(1.05) saturate(0.8)'
  },
  {
    id: 'contrast',
    label: 'Alto contraste',
    css: 'contrast(1.5)'
  },
  {
    id: 'soft',
    label: 'Suave',
    css: 'brightness(1.1) contrast(0.9) saturate(0.9)'
  },
  {
    id: 'invert',
    label: 'Invertido',
    css: 'invert(1)'
  },
  {
    id: 'pink',
    label: 'Rosa',
    css: 'saturate(1.5) hue-rotate(300deg)'
  },
  {
    id: 'neon',
    label: 'Verde neón',
    css: 'saturate(2) hue-rotate(90deg) contrast(1.2)'
  },
  {
    id: 'purple',
    label: 'Morado',
    css: 'saturate(1.6) hue-rotate(250deg)'
  },
  {
    id: 'dramatic',
    label: 'Dramático',
    css: 'contrast(1.4) brightness(0.85) saturate(1.3)'
  },
  {
    id: 'faded',
    label: 'Desvanecido',
    css: 'brightness(1.15) saturate(0.5) contrast(0.85)'
  },
  {
    id: 'night',
    label: 'Azul noche',
    css: 'hue-rotate(200deg) brightness(0.8) saturate(1.3)'
  },
  {
    id: 'golden',
    label: 'Dorado',
    css: 'sepia(0.6) saturate(1.8) brightness(1.05)'
  },
  {
    id: 'polaroid',
    label: 'Polaroid',
    css: 'sepia(0.2) contrast(1.1) brightness(1.1) saturate(1.1)'
  },
  {
    id: 'redpop',
    label: 'Rojo intenso',
    css: 'saturate(1.8) hue-rotate(-20deg) contrast(1.2)'
  }
]
