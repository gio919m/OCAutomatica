interface Props {
  count: number
}

export function SelectionCounter({ count }: Props) {
  return (
    <span className={`pill ${count > 0 ? 'pill-strong' : ''}`}>
      {count === 0
        ? 'Ningun articulo seleccionado'
        : `${count} articulo(s) seleccionado(s)`}
    </span>
  )
}
