import { defineConfig, type Plugin } from 'vite';
import react from '@vitejs/plugin-react';

// In production the .NET web app answers /safety/config.js with the API address and the demo planner key.
// In `npm run dev` this plugin answers it instead, so the app can talk to the API on localhost:5080.
function devConfig(): Plugin {
  return {
    name: 'dev-config-js',
    configureServer(server) {
      server.middlewares.use('/config.js', (_req, res) => {
        res.setHeader('Content-Type', 'application/javascript');
        res.end("window.KRK_CONFIG = { apiBase: 'http://localhost:5080', plannerAutoKey: 'demo-planner' };");
      });
    }
  };
}

// The production build goes straight into the .NET web app's static files, served at /safety/.
export default defineConfig({
  base: './',
  plugins: [react(), devConfig()],
  build: {
    outDir: '../src/KrakowOpenData.Web/wwwroot/safety',
    emptyOutDir: true,
    sourcemap: false,
    chunkSizeWarningLimit: 700
  },
  server: { port: 5173 },
  test: { environment: 'jsdom', setupFiles: ['src/test/setup.ts'], include: ['src/**/*.test.{ts,tsx}'], css: false }
});
