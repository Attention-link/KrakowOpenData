import { act, render } from '@testing-library/react';
import L from 'leaflet';
import { describe, expect, it } from 'vitest';
import { Circle, MapView, Marker, Polyline, dotIcon, endIcon, pinIcon } from './MapView';

describe('map icons', () => {
  it('are cached, so a layer is not rebuilt on every render', () => {
    expect(dotIcon('lamp', 'safety')).toBe(dotIcon('lamp', 'safety'));
    expect(dotIcon('lamp', 'safety')).not.toBe(dotIcon('lamp', 'heat'));
    expect(pinIcon()).toBe(pinIcon());
    expect(endIcon('start', 'Start')).toBe(endIcon('start', 'Start'));
    expect(endIcon('start', 'Start')).not.toBe(endIcon('end', 'Destination'));
  });

  it('draw the A and B of the start and destination markers', () => {
    const start = (endIcon('start', 'Start').options.html as string);
    const end = (endIcon('end', 'Destination').options.html as string);
    expect(start).toContain('>A<');
    expect(start).toContain('Start');
    expect(end).toContain('>B<');
    expect(end).toContain('Destination');
  });
});

describe('MapView and its layers', () => {
  it('shows a marker and removes it when it is no longer rendered', () => {
    const icon = dotIcon('pin', '');
    const { container, rerender } = render(<MapView><Marker position={[50.06, 19.93]} icon={icon} title="Here" /></MapView>);
    expect(container.querySelectorAll('.leaflet-marker-icon')).toHaveLength(1);
    rerender(<MapView>{null}</MapView>);
    expect(container.querySelectorAll('.leaflet-marker-icon')).toHaveLength(0);
  });

  it('moves a marker without rebuilding it (a drag in progress must not be cancelled)', () => {
    const icon = dotIcon('pin', '');
    const { container, rerender } = render(<MapView><Marker position={[50.06, 19.93]} icon={icon} /></MapView>);
    const before = container.querySelector('.leaflet-marker-icon');
    rerender(<MapView><Marker position={[50.07, 19.94]} icon={icon} /></MapView>);
    expect(container.querySelector('.leaflet-marker-icon')).toBe(before);
  });

  it('adds lines and circles to the map', () => {
    let map: L.Map | undefined;
    render(
      <MapView onReady={(m) => { map = m; }}>
        <Polyline positions={[[50.06, 19.93], [50.07, 19.95]]} options={{ color: '#000' }} />
        <Circle center={[50.06, 19.93]} radius={500} options={{ color: '#f00' }} />
      </MapView>
    );
    const layers: L.Layer[] = [];
    map!.eachLayer((l) => layers.push(l));
    expect(layers.some((l) => l instanceof L.Polyline && !(l instanceof L.Polygon))).toBe(true);
    expect(layers.some((l) => l instanceof L.Circle)).toBe(true);
  });

  it('does not rebuild a line when only the identity of its options changes', () => {
    let map: L.Map | undefined;
    const path: [number, number][] = [[50.06, 19.93], [50.07, 19.95]];
    const { rerender } = render(<MapView onReady={(m) => { map = m; }}><Polyline positions={path} options={{ color: '#000' }} /></MapView>);
    const first = (() => { const out: L.Layer[] = []; map!.eachLayer((l) => out.push(l)); return out.find((l) => l instanceof L.Polyline)!; })();
    act(() => { rerender(<MapView onReady={(m) => { map = m; }}><Polyline positions={path} options={{ color: '#000' }} /></MapView>); });
    const second = (() => { const out: L.Layer[] = []; map!.eachLayer((l) => out.push(l)); return out.find((l) => l instanceof L.Polyline)!; })();
    expect(second).toBe(first);
  });
});
