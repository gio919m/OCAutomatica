interface Props {
  count: number
}

export function SelectionCounter({ count }: Props) {
  return (
    <p>
      {count === 0
        ? 'Ningun articulo seleccionado.'
        : `${count} articulo(s) seleccionado(s).`}
    </p>
  )
}
