import { useState } from 'react'
import { Navigate, Route, Routes } from 'react-router-dom'
import Layout from './components/Layout'
import LoginPage from './pages/LoginPage'
import RequireSession from './components/RequireSession'
import ApplicationDetailsPage from './pages/ApplicationDetailsPage'
import ApplicationFormPage from './pages/ApplicationFormPage'
import ApplicationsPage from './pages/ApplicationsPage'
import DashboardPage from './pages/DashboardPage'
import { createApplicationStore } from './data/applicationStore'
import { ApplicationsProvider } from './state/applicationsProvider'

export default function App() {
  // Built once, lazily. ApplicationsProvider's load effect lists `repository` as a dependency,
  // so constructing it inline would re-run the load on every render and re-render forever.
  const [store] = useState(createApplicationStore)

  return (
    <ApplicationsProvider repository={store.repository} storageAvailable={store.storageAvailable}>
      <Layout>
        <Routes>
          <Route path="/" element={<RequireSession><DashboardPage /></RequireSession>} />
          <Route path="/applications" element={<RequireSession><ApplicationsPage /></RequireSession>} />
          {/* Static segment: react-router ranks it above /applications/:id, so "new" is never
              mistaken for an application id. */}
          <Route path="/applications/new" element={<RequireSession><ApplicationFormPage /></RequireSession>} />
          <Route path="/applications/:id" element={<RequireSession><ApplicationDetailsPage /></RequireSession>} />
          <Route path="/applications/:id/edit" element={<RequireSession><ApplicationFormPage /></RequireSession>} />
          {/* Outside RequireSession by necessity: it is where RequireSession sends people. Inside it the redirect is a
              loop -- /login bounced to /login?next=%2Flogin is the classic self-referential guard bug, and the route
              table is the only place it can be prevented, because the guard has no other scope. */}
          <Route path="/login" element={<LoginPage />} />
          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </Layout>
    </ApplicationsProvider>
  )
}
