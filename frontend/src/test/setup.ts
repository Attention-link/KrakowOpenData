import '@testing-library/jest-dom/vitest';
import { cleanup } from '@testing-library/react';
import { afterEach, vi } from 'vitest';

afterEach(() => { cleanup(); localStorage.clear(); sessionStorage.clear(); });

// jsdom has no layout, media queries, ResizeObserver or <dialog> methods: small stand-ins.
window.matchMedia = window.matchMedia || ((query: string) => ({
  matches: false, media: query, onchange: null, addEventListener: () => {}, removeEventListener: () => {}, addListener: () => {}, removeListener: () => {}, dispatchEvent: () => false
}) as unknown as MediaQueryList);

class RO { observe() {} unobserve() {} disconnect() {} }
(globalThis as any).ResizeObserver = (globalThis as any).ResizeObserver || RO;

HTMLDialogElement.prototype.showModal = HTMLDialogElement.prototype.showModal || function (this: HTMLDialogElement) { this.setAttribute('open', ''); };
HTMLDialogElement.prototype.close = HTMLDialogElement.prototype.close || function (this: HTMLDialogElement) { this.removeAttribute('open'); };
Element.prototype.scrollIntoView = Element.prototype.scrollIntoView || vi.fn();

// Leaflet draws lines and areas on a <canvas>; jsdom has none, so give it a context that accepts every call.
const noopContext = new Proxy({}, { get: (_t, prop) => (prop === 'canvas' ? document.createElement('canvas') : () => {}), set: () => true });
HTMLCanvasElement.prototype.getContext = (() => noopContext) as unknown as HTMLCanvasElement['getContext'];
