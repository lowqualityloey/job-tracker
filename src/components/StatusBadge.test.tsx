import { render, screen } from '@testing-library/react'
import StatusBadge from './StatusBadge'

describe('StatusBadge', () => {
  it('renders the provided status text', () => {
    render(<StatusBadge status="Interview" />)

    expect(screen.getByText('Interview')).toBeInTheDocument()
  })

  it('applies the matching status class', () => {
    render(<StatusBadge status="Offer" />)

    expect(screen.getByText('Offer')).toHaveClass('badge-offer')
  })
})
