import { HashRouter, Route, Routes } from 'react-router-dom';
import { DialogHost, ErrorBoundary, Toasts } from './components/ui';
import { useConnectivity, useDocumentSettings } from './components/chrome';
import { PlannerApp } from './planner/PlannerApp';
import { ResidentApp } from './resident/ResidentApp';

export function App() {
  useConnectivity();
  useDocumentSettings();
  return (
    <HashRouter>
      <a className="skip-link" href="#main" data-i18n="skip">Skip to content</a>
      <div className="app">
        <ErrorBoundary>
        <Routes>
          <Route path="/planner/*" element={<PlannerApp />} />
          <Route path="*" element={<ResidentApp />} />
        </Routes>
        </ErrorBoundary>
      </div>
      <div id="live" className="sr-only" aria-live="polite" aria-atomic="true" />
      <DialogHost />
      <Toasts />
    </HashRouter>
  );
}
