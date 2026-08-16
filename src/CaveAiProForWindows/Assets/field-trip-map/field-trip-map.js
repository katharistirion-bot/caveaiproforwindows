(function () {
  'use strict';

  if (typeof maplibregl !== 'undefined' && typeof maplibregl.setWorkerUrl === 'function') {
    maplibregl.setWorkerUrl(new URL('vendor/maplibre-gl-csp-worker.js', window.location.href).href);
  }

  const SOURCE_ID = 'field-trip-route';
  const LAYER_LINE = 'field-trip-line';
  const LAYER_POINTS = 'field-trip-points';

  let map = null;
  let stops = [];

  function postHost(msg) {
    if (window.chrome && window.chrome.webview) {
      window.chrome.webview.postMessage(typeof msg === 'string' ? msg : JSON.stringify(msg));
    }
  }

  function boundsFromStops(list) {
    const lons = list.map((s) => s.lon);
    const lats = list.map((s) => s.lat);
    const pad = 0.02;
    const minLon = Math.min.apply(null, lons) - pad;
    const maxLon = Math.max.apply(null, lons) + pad;
    const minLat = Math.min.apply(null, lats) - pad;
    const maxLat = Math.max.apply(null, lats) + pad;
    return [[minLon, minLat], [maxLon, maxLat]];
  }

  function routeGeoJson(list) {
    const coords = list.map((s) => [s.lon, s.lat]);
    const features = [
      {
        type: 'Feature',
        geometry: { type: 'LineString', coordinates: coords },
        properties: { kind: 'route' },
      },
    ];
    list.forEach((s, i) => {
      features.push({
        type: 'Feature',
        geometry: { type: 'Point', coordinates: [s.lon, s.lat] },
        properties: { index: i + 1, name: s.name || '', isStart: i === 0 },
      });
    });
    return { type: 'FeatureCollection', features };
  }

  function applyStops(list) {
    stops = Array.isArray(list) ? list.filter((s) => s && isFinite(s.lat) && isFinite(s.lon)) : [];
    const empty = document.getElementById('empty');
    if (!map) return;
    if (stops.length === 0) {
      if (empty) empty.classList.remove('hidden');
      if (map.getLayer(LAYER_LINE)) map.removeLayer(LAYER_LINE);
      if (map.getLayer(LAYER_POINTS)) map.removeLayer(LAYER_POINTS);
      if (map.getSource(SOURCE_ID)) map.removeSource(SOURCE_ID);
      return;
    }
    if (empty) empty.classList.add('hidden');
    const gj = routeGeoJson(stops);
    if (!map.getSource(SOURCE_ID)) {
      map.addSource(SOURCE_ID, { type: 'geojson', data: gj });
      map.addLayer({
        id: LAYER_LINE,
        type: 'line',
        source: SOURCE_ID,
        filter: ['==', ['geometry-type'], 'LineString'],
        paint: { 'line-color': '#50a0ff', 'line-width': 2, 'line-dasharray': [2, 2] },
      });
      map.addLayer({
        id: LAYER_POINTS,
        type: 'circle',
        source: SOURCE_ID,
        filter: ['==', ['geometry-type'], 'Point'],
        paint: {
          'circle-radius': 7,
          'circle-color': ['case', ['get', 'isStart'], '#32cd32', '#1e90ff'],
          'circle-stroke-color': '#ffffff',
          'circle-stroke-width': 1.5,
        },
      });
    } else {
      map.getSource(SOURCE_ID).setData(gj);
    }
    map.fitBounds(boundsFromStops(stops), { padding: 36, duration: 0, maxZoom: 14 });
  }

  function initMap() {
    map = new maplibregl.Map({
      container: 'map',
      style: {
        version: 8,
        sources: {
          osm: {
            type: 'raster',
            tiles: ['https://tile.openstreetmap.org/{z}/{x}/{y}.png'],
            tileSize: 256,
            attribution: '© OpenStreetMap',
          },
        },
        layers: [{ id: 'osm', type: 'raster', source: 'osm' }],
      },
      center: [22, 40],
      zoom: 6,
      attributionControl: false,
    });
    map.on('load', () => {
      postHost({ type: 'ready' });
      applyStops(stops);
    });
    map.on('error', (err) => {
      postHost({ type: 'mapError', error: (err && err.error && err.error.message) || 'Map failed to load' });
    });
  }

  function handleHostMessage(data) {
    if (!data || !data.type) return;
    if (data.type === 'stops' && data.payload) {
      applyStops(data.payload.stops || []);
    }
  }

  if (window.chrome && window.chrome.webview) {
    window.chrome.webview.addEventListener('message', (ev) => {
      let data = ev.data;
      if (typeof data === 'string') {
        try { data = JSON.parse(data); } catch (_) { return; }
      }
      handleHostMessage(data);
    });
  }

  window.addEventListener('offline', () => {
    postHost({ type: 'mapOffline' });
    postHost({ type: 'status', message: 'Network offline — using cached OSM tiles when available' });
  });
  if (typeof navigator !== 'undefined' && navigator.onLine === false) {
    postHost({ type: 'mapOffline' });
  }

  initMap();
})();
