import { useState } from 'react'
import { Navigate, Route, Routes } from 'react-router-dom'
import Layout from './components/Layout'
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
          <Route path="/" element={<DashboardPage />} />
          <Route path="/applications" element={<ApplicationsPage />} />
          {/* Static segment: react-router ranks it above /applications/:id, so "new" is never
              mistaken for an application id. */}
          <Route path="/applications/new" element={<ApplicationFormPage />} />
          <Route path="/applications/:id" element={<ApplicationDetailsPage />} />
          <Route path="/applications/:id/edit" element={<ApplicationFormPage />} />
          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </Layout>
    </ApplicationsProvider>
  )
}
