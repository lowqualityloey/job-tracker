import type { PropsWithChildren } from 'react'
import StorageNotice from './StorageNotice'
import { NavLink } from 'react-router-dom'

const navItems = [
  { to: '/', label: 'Dashboard', end: true },
  { to: '/applications', label: 'Applications' },
]

export default function Layout({ children }: PropsWithChildren) {
  return (
    <div className="app-shell">
      <header className="site-header">
        <div>
          <p className="eyebrow">Starter project</p>
          <h1>Job Tracker</h1>
        </div>
        <nav aria-label="Primary navigation">
          <ul className="nav-list">
            {navItems.map((item) => (
              <li key={item.to}>
                <NavLink
                  to={item.to}
                  end={item.end}
                  className={({ isActive }) =>
                    isActive ? 'nav-link nav-link-active' : 'nav-link'
                  }
                >
                  {item.label}
                </NavLink>
              </li>
            ))}
          </ul>
        </nav>
      </header>
      <main className="main-content">
        <StorageNotice />{children}</main>
    </div>
  )
}
