import { useApplications } from '../state/applicationsProvider'

/**
 * Rendered once, near the top of the layout. Without it, a device that blocks storage gives
 * the user a perfectly normal-looking form that discards everything they type — the failure
 * MDN documents for storage that exists but cannot be written.
 */
export default function StorageNotice() {
  const { storageAvailable } = useApplications()

  if (storageAvailable) {
    return null
  }

  return (
    <p className="storage-notice" role="status">
      This device is blocking saved data, so changes will not be saved when you close this tab.
    </p>
  )
}
