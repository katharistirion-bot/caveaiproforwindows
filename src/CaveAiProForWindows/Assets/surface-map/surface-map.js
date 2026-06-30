/* CaveAI Pro ? shared MapLibre surface map (Windows WebView2 + web iframe). */
/* global maplibregl */
(function () {
  'use strict';

  const OSM_TILES = ['https://tile.openstreetmap.org/{z}/{x}/{y}.png'];
  const HILLSHADE_TILES = ['https://tiles.wmflabs.org/hillshading/{z}/{x}/{y}.png'];
  const DEM_TILES = ['https://s3.amazonaws.com/elevation-tiles-prod/terrarium/{z}/{x}/{y}.png'];
  /** EOX Sentinel ? Copernicus GLO-30 DSM hillshade (client-side, no proxy). */
  const COPERNICUS_DSM_TILES = [
    'https://tiles.maps.eox.at/wmts/1.0.0/copernicus_dsm_glo30/default/GoogleMapsCompatible/{z}/{y}/{x}.png',
  ];
  const COPERNICUS_DSM_FALLBACK = [
    'https://tiles.maps.eox.at/wmts/1.0.0/terrain-light/default/GoogleMapsCompatible/{z}/{y}/{x}.jpg',
  ];

  let map = null;
  let project = null;
  const MAX_ELEVATION_SAMPLES = 50;
  const ELEVATION_TILE_CONCURRENCY = 4;
  const LIDAR_MAX_TEXTURE_PX = 4096;
  const RESIZE_DEBOUNCE_MS = 200;
  const ELEVATION_DEBOUNCE_MS = 800;
  const MAP_TILE_CACHE_SIZE = 80;
  const DEM_TILE_CACHE_MAX = 48;

  let hillshadeOn = false;
  let terrain3dOn = false;
  let corridorOn = true;
  let copernicusOn = false;
  let lidarOn = false;
  let performanceMode = false;
  let copernicusFailed = false;
  let persistTimer = null;
  let elevationBusy = false;
  let elevationRunId = 0;
  let mapMoving = false;
  let projectReceived = false;
  let resizeObserver = null;
  let resizeDebounceTimer = null;
  let lastMapWidth = 0;
  let lastMapHeight = 0;
  let lidarObjectUrls = [];
  let lastPersistedMapState = null;
  let suppressPersist = false;

  let measureOn = false;
  let measurePoints = [];
  let pinPickKind = null;
  let entrancePinOn = true;
  let vehiclePinsOn = true;
  let lidarOpacity = 0.55;
  let lastCursor = null;
  let interactionsBound = false;

  function postHost(msg) {
    if (window.chrome && window.chrome.webview) {
      window.chrome.webview.postMessage(msg);
    }
    if (window.parent && window.parent !== window) {
      window.parent.postMessage(msg, window.location.origin);
    }
  }

  function defaultCenter() {
    const ent = entranceLonLat();
    if (ent) return [ent.lon, ent.lat];
    return [23.7275, 37.9838];
  }

  function defaultZoom() {
    if (hasEntrance()) return 15;
    return 6;
  }

  function parseCoord(value) {
    if (value == null) return null;
    if (typeof value === 'number') return Number.isFinite(value) ? value : null;
    const s = String(value).trim();
    if (!s || s === 'N/A') return null;
    const n = Number(s);
    return Number.isFinite(n) ? n : null;
  }

  function readEntranceFromPayload(p) {
    if (!p || typeof p !== 'object') return null;
    const pairs = [
      ['lat', 'lon'],
      ['entranceLat', 'entranceLon'],
      ['latitude', 'longitude'],
    ];
    for (let i = 0; i < pairs.length; i += 1) {
      const latKey = pairs[i][0];
      const lonKey = pairs[i][1];
      const lat = parseCoord(p[latKey]);
      const lon = parseCoord(p[lonKey]);
      if (lat == null || lon == null) continue;
      if (lat <= -90 || lat >= 90 || lon <= -180 || lon >= 180) continue;
      if (Math.abs(lat) < 1e-12 && Math.abs(lon) < 1e-12) continue;
      return { lat, lon };
    }
    return null;
  }

  function hasEntrance() {
    return readEntranceFromPayload(project) != null;
  }

  function entranceLonLat() {
    const ent = readEntranceFromPayload(project);
    if (!ent) return null;
    return { lon: ent.lon, lat: ent.lat };
  }

  function updateEmptyState() {
    const panel = document.getElementById('empty-state');
    const title = document.getElementById('empty-state-title');
    const hint = document.getElementById('empty-state-hint');
    if (!panel) return;
    if (!projectReceived || hasEntrance()) {
      panel.classList.add('hidden');
      return;
    }
    panel.classList.remove('hidden');
    const name = project && project.name ? String(project.name) : 'This project';
    if (title) title.textContent = `${name}: no entrance coordinates`;
    const customHint = project && project.emptyStateHint;
    if (hint) {
      hint.textContent = customHint ||
        'Lock the survey entrance (A1) in CaveAI Pro on Android (Entrance & Surface Tracking) or set lat/lon in Windows project settings, then reload this map.';
    }
  }


  function setMapBusy(busy, message) {
    const el = document.getElementById('map-busy');
    const text = document.getElementById('map-busy-text');
    if (!el) return;
    el.classList.toggle('hidden', !busy);
    if (text && message) text.textContent = message;
  }

  function readLayerDefaults(ms) {
    if (performanceMode) {
      hillshadeOn = false;
      terrain3dOn = false;
      corridorOn = false;
      copernicusOn = false;
      lidarOn = false;
      return;
    }
    hillshadeOn = ms.hillshadeEnabled === true;
    terrain3dOn = !!ms.terrain3dEnabled;
    corridorOn = ms.corridorOverlayEnabled !== false;
    copernicusOn = !!ms.copernicusDsmEnabled;
    lidarOn = ms.lidarOverlayEnabled === true;
    if (typeof ms.lidarOpacity === 'number' && isFinite(ms.lidarOpacity)) {
      lidarOpacity = Math.min(1, Math.max(0.05, ms.lidarOpacity));
    }
    if (typeof ms.entrancePinEnabled === 'boolean') entrancePinOn = ms.entrancePinEnabled;
    if (typeof ms.vehiclePinsEnabled === 'boolean') vehiclePinsOn = ms.vehiclePinsEnabled;
  }

  function heavyLayersActive() {
    let n = 0;
    if (hillshadeOn) n += 1;
    if (terrain3dOn) n += 1;
    if (copernicusOn) n += 1;
    if (lidarOn) n += 1;
    return n >= 3;
  }

  function maybeWarnHeavyLayers() {
    if (performanceMode || !heavyLayersActive()) return;
    postHost({
      type: 'status',
      message: 'Many heavy layers enabled — map may lag. Try Performance mode.',
    });
  }

  function bindWebGlRecovery() {
    if (!map) return;
    const canvas = map.getCanvas();
    if (!canvas || canvas.__caveAiGlBound) return;
    canvas.__caveAiGlBound = true;
    canvas.addEventListener('webglcontextlost', (ev) => {
      ev.preventDefault();
      setMapBusy(true, 'Graphics paused — enable Performance mode or reload.');
      postHost({
        type: 'status',
        message: 'WebGL context lost. Try Performance mode or reload the map.',
      });
    });
    canvas.addEventListener('webglcontextrestored', () => {
      setMapBusy(false);
      postHost({ type: 'status', message: 'Graphics restored' });
      scheduleMapResize();
    });
  }

  function mapStateNearEnough(prev, next) {
    if (!prev || !next) return false;
    return (
      Math.abs((prev.centerLon || 0) - (next.centerLon || 0)) < 1e-6
      && Math.abs((prev.centerLat || 0) - (next.centerLat || 0)) < 1e-6
      && Math.abs((prev.zoom || 0) - (next.zoom || 0)) < 1e-5
      && Math.abs((prev.bearing || 0) - (next.bearing || 0)) < 1e-4
      && Math.abs((prev.pitch || 0) - (next.pitch || 0)) < 1e-4
      && prev.hillshadeEnabled === next.hillshadeEnabled
      && prev.terrain3dEnabled === next.terrain3dEnabled
      && prev.corridorOverlayEnabled === next.corridorOverlayEnabled
      && prev.copernicusDsmEnabled === next.copernicusDsmEnabled
      && prev.lidarOverlayEnabled === next.lidarOverlayEnabled
      && prev.performanceMode === next.performanceMode
    );
  }

  function scheduleMapResize() {
    if (!map) return;
    clearTimeout(resizeDebounceTimer);
    resizeDebounceTimer = setTimeout(() => {
      const mapEl = document.getElementById('map');
      if (!mapEl) return;
      const w = mapEl.clientWidth;
      const h = mapEl.clientHeight;
      if (w < 1 || h < 1) return;
      if (w === lastMapWidth && h === lastMapHeight) return;
      lastMapWidth = w;
      lastMapHeight = h;
      suppressPersist = true;
      requestAnimationFrame(() => {
        try {
          map.resize();
        } catch {
          /* ignore */
        }
        map.once('moveend', () => {
          suppressPersist = false;
        });
        setTimeout(() => {
          suppressPersist = false;
        }, RESIZE_DEBOUNCE_MS + 50);
      });
    }, RESIZE_DEBOUNCE_MS);
  }

  function bindMapResize() {
    if (window.__caveAiSurfaceMapResizeBound) return;
    window.__caveAiSurfaceMapResizeBound = true;
    window.addEventListener('resize', scheduleMapResize);
    const mapEl = document.getElementById('map');
    if (!mapEl || resizeObserver || typeof ResizeObserver === 'undefined') return;
    resizeObserver = new ResizeObserver(() => scheduleMapResize());
    resizeObserver.observe(mapEl);
  }

  function ensureMap() {
    if (map) return map;
    const ms = (project && project.mapState) || {};
    performanceMode = !!ms.performanceMode;
    readLayerDefaults(ms);

    map = new maplibregl.Map({
      container: 'map',
      style: {
        version: 8,
        sources: {},
        layers: [],
        glyphs: 'https://demotiles.maplibre.org/font/{fontstack}/{range}.pbf',
      },
      center: [ms.centerLon || defaultCenter()[0], ms.centerLat || defaultCenter()[1]],
      zoom: ms.zoom || defaultZoom(),
      bearing: ms.bearing || 0,
      pitch: ms.pitch || 0,
      maxPitch: terrain3dOn ? 65 : 0,
      attributionControl: true,
      preserveDrawingBuffer: true,
      maxTileCacheSize: MAP_TILE_CACHE_SIZE,
      refreshExpiredTiles: false,
    });

    map.addControl(new maplibregl.NavigationControl({ visualizePitch: true }), 'top-right');

    map.on('load', () => {
      addBasemap();
      addHillshade();
      addCopernicusDsm();
      addTerrain();
      applyEntrancePin();
      applyVehiclePins();
      applySurveyCorridor();
      applyLidarRaster();
      bindHud();
      bindWebGlRecovery();
      updateEmptyState();
      schedulePersist();
      scheduleElevationProfile();
      bindMapResize();
      scheduleMapResize();
      setTimeout(scheduleMapResize, 300);
      maybeWarnHeavyLayers();
      postHost({ type: 'ready' });
    });

    map.on('movestart', () => {
      mapMoving = true;
      elevationRunId += 1;
      hideElevationPanel();
    });
    map.on('moveend', () => {
      mapMoving = false;
      schedulePersist();
      scheduleElevationProfile();
    });
    return map;
  }

  function addBasemap() {
    if (map.getSource('osm')) return;
    map.addSource('osm', {
      type: 'raster',
      tiles: OSM_TILES,
      tileSize: 256,
      maxzoom: 19,
      attribution: '? OpenStreetMap contributors',
    });
    map.addLayer({ id: 'osm', type: 'raster', source: 'osm' });
  }

  function addHillshade() {
    if (map.getSource('hillshade')) {
      map.setLayoutProperty('hillshade', 'visibility', hillshadeOn ? 'visible' : 'none');
      return;
    }
    map.addSource('hillshade', {
      type: 'raster',
      tiles: HILLSHADE_TILES,
      tileSize: 256,
      maxzoom: 15,
      attribution: '? Wikimedia maps',
    });
    map.addLayer({
      id: 'hillshade',
      type: 'raster',
      source: 'hillshade',
      paint: { 'raster-opacity': 0.45 },
      layout: { visibility: hillshadeOn ? 'visible' : 'none' },
    });
  }

  function addCopernicusDsm() {
    const layerId = 'copernicus-dsm';
    if (!copernicusOn) {
      if (map.getLayer(layerId)) map.setLayoutProperty(layerId, 'visibility', 'none');
      return;
    }
    const tiles = copernicusFailed ? COPERNICUS_DSM_FALLBACK : COPERNICUS_DSM_TILES;
    if (map.getSource('copernicus-dsm')) {
      map.setLayoutProperty(layerId, 'visibility', 'visible');
      return;
    }
    map.addSource('copernicus-dsm', {
      type: 'raster',
      tiles,
      tileSize: 256,
      maxzoom: 14,
      attribution: '? Copernicus / EOX',
    });
    map.addLayer({
      id: layerId,
      type: 'raster',
      source: 'copernicus-dsm',
      paint: { 'raster-opacity': 0.5 },
      layout: { visibility: 'visible' },
    });
    map.on('error', (e) => {
      if (!copernicusOn || copernicusFailed) return;
      const src = e && e.error && e.error.message;
      if (src && String(src).toLowerCase().includes('copernicus')) {
        copernicusFailed = true;
        if (map.getLayer(layerId)) map.removeLayer(layerId);
        if (map.getSource('copernicus-dsm')) map.removeSource('copernicus-dsm');
        addCopernicusDsm();
        postHost({ type: 'status', message: 'Copernicus DSM unavailable ? using terrain-light fallback' });
      }
    });
  }

  function removeDemSource() {
    if (map.getTerrain()) map.setTerrain(null);
    if (map.getSource('dem')) map.removeSource('dem');
  }

  function addTerrain() {
    if (!terrain3dOn) {
      removeDemSource();
      map.setMaxPitch(0);
      return;
    }
    map.setMaxPitch(65);
    if (!map.getSource('dem')) {
      map.addSource('dem', {
        type: 'raster-dem',
        tiles: DEM_TILES,
        tileSize: 256,
        maxzoom: 13,
        encoding: 'terrarium',
        attribution: '? Mapzen / AWS Terrain Tiles',
      });
    }
    map.setTerrain({ source: 'dem', exaggeration: 1.2 });
    if (!map.getLayer('hillshade') && hillshadeOn) addHillshade();
  }

  function updateGeoJsonSource(srcId, data) {
    const src = map.getSource(srcId);
    if (src && typeof src.setData === 'function') { src.setData(data); return true; }
    return false;
  }

  function formatDistanceM(m) {
    if (!isFinite(m)) return '—';
    if (m >= 1000) return (m / 1000).toFixed(2) + ' km';
    return Math.round(m) + ' m';
  }

  function declinationNote() {
    const ent = entranceLonLat();
    if (!ent) return '';
    const d = project && project.declinationDeg;
    if (typeof d === 'number' && isFinite(d)) {
      return 'Magnetic declination: ' + d.toFixed(1) + '° (survey calibration)';
    }
    const rough = 0.3 + 0.08 * ent.lat + 0.05 * Math.sin((ent.lon * Math.PI) / 90);
    return 'Magnetic declination: ~' + rough.toFixed(1) + '° (approximate — calibrate survey for exact value)';
  }

  function applyEntrancePin() {
    const srcId = 'entrance';
    const layerId = 'entrance-pin';
    const ent = entranceLonLat();
    if (!ent || !entrancePinOn) {
      if (map.getLayer('entrance-label')) map.removeLayer('entrance-label');
      if (map.getLayer(layerId)) map.removeLayer(layerId);
      if (map.getSource(srcId)) map.removeSource(srcId);
      return;
    }
    const label = project.name ? String(project.name) : 'Entrance';
    const feature = { type: 'Feature', geometry: { type: 'Point', coordinates: [ent.lon, ent.lat] }, properties: { title: label } };
    if (updateGeoJsonSource(srcId, feature)) return;
    if (map.getLayer('entrance-label')) map.removeLayer('entrance-label');
    if (map.getLayer(layerId)) map.removeLayer(layerId);
    if (map.getSource(srcId)) map.removeSource(srcId);
    map.addSource(srcId, { type: 'geojson', data: feature });
    map.addLayer({
      id: layerId,
      type: 'circle',
      source: srcId,
      paint: {
        'circle-radius': 9,
        'circle-color': '#ff6b35',
        'circle-stroke-width': 2,
        'circle-stroke-color': '#ffffff',
      },
    });
    map.addLayer({
      id: 'entrance-label',
      type: 'symbol',
      source: srcId,
      layout: {
        'text-field': ['get', 'title'],
        'text-offset': [0, 1.2],
        'text-size': 12,
        'text-anchor': 'top',
      },
      paint: {
        'text-color': '#ffffff',
        'text-halo-color': '#000000',
        'text-halo-width': 1.2,
      },
    });
  }

  function applyVehiclePins() {
    const layerIds = ['vehicle-pins', 'vehicle-labels'];
    layerIds.forEach((id) => {
      if (map.getLayer(id)) map.removeLayer(id);
    });
    if (map.getSource('vehicle-pins')) map.removeSource('vehicle-pins');

    if (!vehiclePinsOn) return;

    const features = [];
    const car = project && project.returnCar;
    const base = project && project.returnBase;
    if (car && isFinite(car.lat) && isFinite(car.lon)) {
      features.push({
        type: 'Feature',
        geometry: { type: 'Point', coordinates: [car.lon, car.lat] },
        properties: { title: 'Vehicle park', kind: 'vehicle' },
      });
    }
    if (base && isFinite(base.lat) && isFinite(base.lon)) {
      features.push({
        type: 'Feature',
        geometry: { type: 'Point', coordinates: [base.lon, base.lat] },
        properties: { title: 'Trailhead / base', kind: 'base' },
      });
    }
    if (features.length === 0) {
      if (map.getLayer('vehicle-labels')) map.removeLayer('vehicle-labels');
      if (map.getLayer('vehicle-pins')) map.removeLayer('vehicle-pins');
      if (map.getSource('vehicle-pins')) map.removeSource('vehicle-pins');
      return;
    }
    const collection = { type: 'FeatureCollection', features };
    if (updateGeoJsonSource('vehicle-pins', collection)) return;
    map.addSource('vehicle-pins', { type: 'geojson', data: collection });
    map.addLayer({
      id: 'vehicle-pins',
      type: 'circle',
      source: 'vehicle-pins',
      paint: {
        'circle-radius': 8,
        'circle-color': [
          'match',
          ['get', 'kind'],
          'vehicle',
          '#58a6ff',
          'base',
          '#d2a8ff',
          '#58a6ff',
        ],
        'circle-stroke-width': 2,
        'circle-stroke-color': '#ffffff',
      },
    });
    map.addLayer({
      id: 'vehicle-labels',
      type: 'symbol',
      source: 'vehicle-pins',
      layout: {
        'text-field': ['get', 'title'],
        'text-offset': [0, 1.1],
        'text-size': 11,
        'text-anchor': 'top',
      },
      paint: {
        'text-color': '#e6edf3',
        'text-halo-color': '#000000',
        'text-halo-width': 1,
      },
    });
  }

  function applySurveyCorridor() {
    const lineLayer = 'survey-corridor-line';
    const startLayer = 'survey-corridor-start';
    const endLayer = 'survey-corridor-end';
    [lineLayer, startLayer, endLayer].forEach((id) => {
      if (map.getLayer(id)) map.removeLayer(id);
    });
    ['survey-corridor', 'survey-corridor-endpoints'].forEach((id) => {
      if (map.getSource(id)) map.removeSource(id);
    });

    if (!corridorOn) return;

    const corridor = project && project.surveyCorridor;
    if (!corridor || corridor.type !== 'FeatureCollection' || !Array.isArray(corridor.features)) return;

    const lineFeatures = corridor.features.filter(
      (f) => f && f.geometry && f.geometry.type === 'LineString' &&
        Array.isArray(f.geometry.coordinates) && f.geometry.coordinates.length >= 2
    );
    if (lineFeatures.length === 0) {
      [lineLayer, startLayer, endLayer].forEach((id) => { if (map.getLayer(id)) map.removeLayer(id); });
      ['survey-corridor', 'survey-corridor-endpoints'].forEach((id) => { if (map.getSource(id)) map.removeSource(id); });
      return;
    }
    const corridorData = { type: 'FeatureCollection', features: lineFeatures };
    if (updateGeoJsonSource('survey-corridor', corridorData)) {
      const endpoints = [];
      lineFeatures.forEach((f) => {
        const coords = f.geometry.coordinates;
        endpoints.push({ type: 'Feature', geometry: { type: 'Point', coordinates: coords[0] }, properties: { role: 'start' } });
        endpoints.push({ type: 'Feature', geometry: { type: 'Point', coordinates: coords[coords.length - 1] }, properties: { role: 'end' } });
      });
      updateGeoJsonSource('survey-corridor-endpoints', { type: 'FeatureCollection', features: endpoints });
      return;
    }
    map.addSource('survey-corridor', { type: 'geojson', data: corridorData });
    map.addLayer({
      id: lineLayer,
      type: 'line',
      source: 'survey-corridor',
      paint: {
        'line-color': '#00d4aa',
        'line-width': 4,
        'line-opacity': 0.85,
      },
      layout: { 'line-cap': 'round', 'line-join': 'round' },
    });

    const endpoints = [];
    lineFeatures.forEach((f) => {
      const coords = f.geometry.coordinates;
      endpoints.push({ type: 'Feature', geometry: { type: 'Point', coordinates: coords[0] }, properties: { role: 'start' } });
      endpoints.push({ type: 'Feature', geometry: { type: 'Point', coordinates: coords[coords.length - 1] }, properties: { role: 'end' } });
    });
    map.addSource('survey-corridor-endpoints', {
      type: 'geojson',
      data: { type: 'FeatureCollection', features: endpoints },
    });
    map.addLayer({
      id: startLayer,
      type: 'circle',
      source: 'survey-corridor-endpoints',
      filter: ['==', ['get', 'role'], 'start'],
      paint: {
        'circle-radius': 5,
        'circle-color': '#3fb950',
        'circle-stroke-width': 1.5,
        'circle-stroke-color': '#ffffff',
      },
    });
    map.addLayer({
      id: endLayer,
      type: 'circle',
      source: 'survey-corridor-endpoints',
      filter: ['==', ['get', 'role'], 'end'],
      paint: {
        'circle-radius': 5,
        'circle-color': '#f85149',
        'circle-stroke-width': 1.5,
        'circle-stroke-color': '#ffffff',
      },
    });
  }

  function revokeLidarObjectUrls() {
    lidarObjectUrls.forEach((url) => { try { URL.revokeObjectURL(url); } catch { /* ignore */ } });
    lidarObjectUrls = [];
  }

  function downscaleImageUrl(url) {
    return new Promise((resolve) => {
      const img = new Image();
      img.crossOrigin = 'anonymous';
      img.onload = () => {
        const maxDim = Math.max(img.naturalWidth, img.naturalHeight);
        if (maxDim <= LIDAR_MAX_TEXTURE_PX) { resolve(url); return; }
        const scale = LIDAR_MAX_TEXTURE_PX / maxDim;
        const w = Math.max(1, Math.round(img.naturalWidth * scale));
        const h = Math.max(1, Math.round(img.naturalHeight * scale));
        const canvas = document.createElement('canvas');
        canvas.width = w; canvas.height = h;
        canvas.getContext('2d').drawImage(img, 0, 0, w, h);
        canvas.toBlob((blob) => {
          if (!blob) { resolve(url); return; }
          const objectUrl = URL.createObjectURL(blob);
          lidarObjectUrls.push(objectUrl);
          resolve(objectUrl);
        }, 'image/png');
      };
      img.onerror = () => resolve(url);
      img.src = url;
    });
  }

  function applyLidarRaster() {
    const ids = ['lidar-raster', 'lidar-bounds'];
    ids.forEach((id) => { if (map.getLayer(id)) map.removeLayer(id); });
    if (map.getSource('lidar-raster')) map.removeSource('lidar-raster');
    if (map.getSource('lidar-bounds')) map.removeSource('lidar-bounds');
    revokeLidarObjectUrls();

    const lr = project && project.surfaceLidarRaster;
    if (!lr || !lidarOn) return;

    const swLat = lr.southWestLat;
    const swLon = lr.southWestLon;
    const neLat = lr.northEastLat;
    const neLon = lr.northEastLon;
    if (!isFinite(swLat) || !isFinite(swLon) || !isFinite(neLat) || !isFinite(neLon)) return;

    map.addSource('lidar-bounds', {
      type: 'geojson',
      data: {
        type: 'Feature',
        geometry: {
          type: 'Polygon',
          coordinates: [[
            [swLon, swLat],
            [neLon, swLat],
            [neLon, neLat],
            [swLon, neLat],
            [swLon, swLat],
          ]],
        },
      },
    });
    map.addLayer({
      id: 'lidar-bounds',
      type: 'line',
      source: 'lidar-bounds',
      paint: {
        'line-color': '#58a6ff',
        'line-dasharray': [2, 2],
        'line-width': 2,
      },
    });

    if (lr.imageUrl) {
      const coords = [[swLon, neLat], [neLon, neLat], [neLon, swLat], [swLon, swLat]];
      const addLidarSource = (imageUrl) => {
        if (map.getSource('lidar-raster')) return;
        map.addSource('lidar-raster', { type: 'image', url: imageUrl, coordinates: coords });
        const op = lidarOpacity != null ? lidarOpacity : (lr.opacity != null ? lr.opacity : 0.55);
        map.addLayer({ id: 'lidar-raster', type: 'raster', source: 'lidar-raster', paint: { 'raster-opacity': op } });
      };
      setMapBusy(true, 'Preparing LiDAR overlay…');
      downscaleImageUrl(lr.imageUrl).then((imageUrl) => { setMapBusy(false); addLidarSource(imageUrl); }).catch(() => { setMapBusy(false); addLidarSource(lr.imageUrl); });
      const onLidarError = (ev) => {
        if (ev && ev.sourceId === 'lidar-raster') {
          map.off('error', onLidarError);
          postHost({ type: 'status', message: 'LiDAR image failed to load' });
        }
      };
      map.on('error', onLidarError);
    } else if (project.lidarStatusHint) {
      postHost({ type: 'status', message: project.lidarStatusHint });
    }
  }

  function fitEntrance() {
    if (!map) return;
    suppressPersist = true;
    const releasePersist = () => {
      suppressPersist = false;
    };
    const ent = entranceLonLat();
    if (ent) {
      map.jumpTo({ center: [ent.lon, ent.lat], zoom: 16 });
      map.once('moveend', releasePersist);
      return;
    }
    const lr = project && project.surfaceLidarRaster;
    if (lr && isFinite(lr.southWestLat)) {
      map.fitBounds(
        [[lr.southWestLon, lr.southWestLat], [lr.northEastLon, lr.northEastLat]],
        { padding: 40, maxZoom: 17, duration: 0 }
      );
      map.once('moveend', releasePersist);
      return;
    }
    releasePersist();
  }

  function collectSurveyExtentPoints() {
    const pts = [];
    const ent = entranceLonLat();
    if (ent) pts.push([ent.lon, ent.lat]);
    const car = project && project.returnCar;
    const base = project && project.returnBase;
    if (car && isFinite(car.lat) && isFinite(car.lon)) pts.push([car.lon, car.lat]);
    if (base && isFinite(base.lat) && isFinite(base.lon)) pts.push([base.lon, base.lat]);
    const corridor = project && project.surveyCorridor;
    if (corridor && corridor.type === 'FeatureCollection' && Array.isArray(corridor.features)) {
      corridor.features.forEach((f) => {
        if (!f || !f.geometry || f.geometry.type !== 'LineString') return;
        (f.geometry.coordinates || []).forEach((c) => {
          if (Array.isArray(c) && c.length >= 2 && isFinite(c[0]) && isFinite(c[1])) pts.push([c[0], c[1]]);
        });
      });
    }
    const lr = project && project.surfaceLidarRaster;
    if (lr && isFinite(lr.southWestLat)) {
      pts.push([lr.southWestLon, lr.southWestLat], [lr.northEastLon, lr.northEastLat]);
    }
    return pts;
  }

  function fitSurvey() {
    if (!map) return;
    const pts = collectSurveyExtentPoints();
    if (pts.length === 0) {
      fitEntrance();
      return;
    }
    if (pts.length === 1) {
      suppressPersist = true;
      map.jumpTo({ center: pts[0], zoom: 16 });
      map.once('moveend', () => { suppressPersist = false; });
      return;
    }
    const bounds = pts.reduce(
      (b, p) => b.extend(p),
      new maplibregl.LngLatBounds(pts[0], pts[0])
    );
    suppressPersist = true;
    map.fitBounds(bounds, { padding: 48, maxZoom: 17, duration: 0 });
    map.once('moveend', () => { suppressPersist = false; });
  }

  function ensureMeasureLayers() {
    if (!map) return;
    if (!map.getSource('measure')) {
      map.addSource('measure', { type: 'geojson', data: { type: 'FeatureCollection', features: [] } });
      map.addLayer({
        id: 'measure-line',
        type: 'line',
        source: 'measure',
        filter: ['==', '$type', 'LineString'],
        paint: { 'line-color': '#f0883e', 'line-width': 3, 'line-dasharray': [2, 1] },
        layout: { visibility: measureOn ? 'visible' : 'none' },
      });
      map.addLayer({
        id: 'measure-points',
        type: 'circle',
        source: 'measure',
        filter: ['==', '$type', 'Point'],
        paint: {
          'circle-radius': 6,
          'circle-color': '#f0883e',
          'circle-stroke-width': 2,
          'circle-stroke-color': '#ffffff',
        },
        layout: { visibility: measureOn ? 'visible' : 'none' },
      });
    }
  }

  function updateMeasureOverlay() {
    if (!map || !map.getSource('measure')) return;
    const features = [];
    if (measurePoints.length >= 1) {
      features.push({
        type: 'Feature',
        geometry: { type: 'Point', coordinates: [measurePoints[0].lon, measurePoints[0].lat] },
        properties: {},
      });
    }
    if (measurePoints.length >= 2) {
      features.push({
        type: 'Feature',
        geometry: { type: 'Point', coordinates: [measurePoints[1].lon, measurePoints[1].lat] },
        properties: {},
      });
      features.push({
        type: 'Feature',
        geometry: {
          type: 'LineString',
          coordinates: [
            [measurePoints[0].lon, measurePoints[0].lat],
            [measurePoints[1].lon, measurePoints[1].lat],
          ],
        },
        properties: {},
      });
    }
    map.getSource('measure').setData({ type: 'FeatureCollection', features });
    const vis = measureOn ? 'visible' : 'none';
    if (map.getLayer('measure-line')) {
      map.setLayoutProperty('measure-line', 'visibility', vis);
      map.setLayoutProperty('measure-points', 'visibility', vis);
    }
    const readout = document.getElementById('measure-readout');
    if (measurePoints.length >= 2) {
      const dist = haversineM(
        measurePoints[0].lon, measurePoints[0].lat,
        measurePoints[1].lon, measurePoints[1].lat
      );
      const label = formatDistanceM(dist);
      if (readout) {
        readout.textContent = 'Distance: ' + label;
        readout.classList.remove('hidden');
      }
      postHost({ type: 'measureResult', metres: dist, label });
    } else {
      if (readout) readout.classList.add('hidden');
      postHost({ type: 'measureResult', metres: null, label: '' });
    }
  }

  function setMeasureMode(active) {
    measureOn = !!active;
    if (!measureOn) measurePoints = [];
    if (map) {
      ensureMeasureLayers();
      updateMeasureOverlay();
      map.getCanvas().style.cursor = measureOn || pinPickKind ? 'crosshair' : '';
    }
    document.body.classList.toggle('surface-map--measure', measureOn);
    postHost({ type: 'measureMode', active: measureOn });
  }

  function setLidarOpacityValue(opacity) {
    if (!isFinite(opacity)) return;
    lidarOpacity = Math.min(1, Math.max(0.05, opacity));
    if (map && map.getLayer('lidar-raster')) {
      map.setPaintProperty('lidar-raster', 'raster-opacity', lidarOpacity);
    }
    schedulePersist();
  }

  function corridorWaypoint(which) {
    const coords = collectCorridorCoords();
    if (coords.length === 0) return null;
    if (which === 'start') return coords[0];
    if (which === 'end') return coords[coords.length - 1];
    return null;
  }

  function resolveCoordsRequest(source) {
    if (source === 'entrance') return entranceLonLat();
    if (source === 'cursor' && lastCursor) return lastCursor;
    if (source === 'corridor-start' || source === 'corridor-end') {
      return corridorWaypoint(source === 'corridor-start' ? 'start' : 'end');
    }
    return lastCursor || entranceLonLat();
  }

  function escapeXml(s) {
    return String(s)
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;');
  }

  function buildCorridorGpx() {
    const coords = collectCorridorCoords();
    const name = project && project.name ? String(project.name) : 'Survey corridor';
    if (coords.length < 2) {
      return '<?xml version="1.0" encoding="UTF-8"?>\n<gpx version="1.1" creator="CaveAI Pro Surface map" xmlns="http://www.topografix.com/GPX/1/1">\n</gpx>\n';
    }
    const trkpts = coords.map((c) => {
      return '      <trkpt lat="' + c.lat.toFixed(6) + '" lon="' + c.lon.toFixed(6) + '"></trkpt>';
    }).join('\n');
    return '<?xml version="1.0" encoding="UTF-8"?>\n<gpx version="1.1" creator="CaveAI Pro Surface map" xmlns="http://www.topografix.com/GPX/1/1">\n  <trk>\n    <name>' + escapeXml(name) + ' corridor</name>\n    <trkseg>\n' + trkpts + '\n    </trkseg>\n  </trk>\n</gpx>\n';
  }

  function buildCorridorGeoJson() {
    const corridor = project && project.surveyCorridor;
    if (corridor && corridor.type === 'FeatureCollection') return corridor;
    const coords = collectCorridorCoords();
    if (coords.length < 2) {
      return { type: 'FeatureCollection', features: [] };
    }
    return {
      type: 'FeatureCollection',
      features: [{
        type: 'Feature',
        properties: { name: (project && project.name) || 'Survey corridor' },
        geometry: {
          type: 'LineString',
          coordinates: coords.map((c) => [c.lon, c.lat]),
        },
      }],
    };
  }

  function exportPackage() {
    if (!map) {
      postHost({ type: 'exportPackageResult', error: 'Map not ready' });
      return;
    }
    try {
      const dataUrl = map.getCanvas().toDataURL('image/png');
      postHost({
        type: 'exportPackageResult',
        dataUrl,
        geoJson: buildCorridorGeoJson(),
        gpx: buildCorridorGpx(),
        projectName: project && project.name ? String(project.name) : 'surface-map',
      });
    } catch (err) {
      postHost({ type: 'exportPackageResult', error: String(err && err.message ? err.message : err) });
    }
  }

  function placePinFromGps(kind) {
    if (!navigator.geolocation) {
      postHost({ type: 'status', message: 'GPS unavailable in this browser context' });
      return;
    }
    setMapBusy(true, 'Reading GPS…');
    navigator.geolocation.getCurrentPosition(
      (pos) => {
        setMapBusy(false);
        const lat = pos.coords.latitude;
        const lon = pos.coords.longitude;
        postHost({ type: 'pinPlaced', kind, lat, lon, source: 'gps' });
        if (kind === 'vehicle') {
          project.returnCar = { lat, lon };
        } else if (kind === 'base') {
          project.returnBase = { lat, lon };
        }
        applyVehiclePins();
        postHost({ type: 'status', message: (kind === 'vehicle' ? 'Vehicle park' : 'Trailhead') + ' pin set from GPS' });
      },
      () => {
        setMapBusy(false);
        postHost({ type: 'status', message: 'GPS fix failed — pick on map instead' });
      },
      { enableHighAccuracy: true, timeout: 15000 }
    );
  }

  function bindMapInteractions() {
    if (!map || interactionsBound) return;
    interactionsBound = true;
    ensureMeasureLayers();

    map.on('click', (e) => {
      if (measureOn) {
        const pt = { lon: e.lngLat.lng, lat: e.lngLat.lat };
        if (measurePoints.length >= 2) measurePoints = [];
        measurePoints.push(pt);
        updateMeasureOverlay();
        return;
      }
      if (pinPickKind) {
        const kind = pinPickKind;
        pinPickKind = null;
        map.getCanvas().style.cursor = measureOn ? 'crosshair' : '';
        const lat = e.lngLat.lat;
        const lon = e.lngLat.lng;
        if (kind === 'vehicle') project.returnCar = { lat, lon };
        else if (kind === 'base') project.returnBase = { lat, lon };
        applyVehiclePins();
        postHost({ type: 'pinPlaced', kind, lat, lon, source: 'map' });
        postHost({ type: 'status', message: (kind === 'vehicle' ? 'Vehicle park' : 'Trailhead') + ' pin placed — save project to persist' });
        postHost({ type: 'pinPickMode', active: false, kind: null });
      }
    });
  }

  function bindHud() {
    const hud = document.getElementById('hud');
    const coords = document.getElementById('hud-coords');
    const decl = document.getElementById('hud-declination');
    if (!hud || !coords) return;
    hud.classList.remove('hidden');
    bindMapInteractions();
    map.on('mousemove', (e) => {
      lastCursor = { lon: e.lngLat.lng, lat: e.lngLat.lat };
      coords.textContent = e.lngLat.lat.toFixed(5) + '\u00b0N, ' + e.lngLat.lng.toFixed(5) + '\u00b0E';
      postHost({ type: 'cursorCoords', lat: e.lngLat.lat, lon: e.lngLat.lng });
      if (decl && hasEntrance()) decl.textContent = declinationNote();
    });
    map.on('mouseout', () => {
      postHost({ type: 'cursorCoords', lat: null, lon: null });
    });
    if (decl && hasEntrance()) decl.textContent = declinationNote();
  }

  function schedulePersist() {
    if (!map || suppressPersist) return;
    clearTimeout(persistTimer);
    persistTimer = setTimeout(() => {
      if (suppressPersist) return;
      const c = map.getCenter();
      const payload = {
        hillshadeEnabled: hillshadeOn,
        terrain3dEnabled: terrain3dOn,
        corridorOverlayEnabled: corridorOn,
        copernicusDsmEnabled: copernicusOn,
        lidarOverlayEnabled: lidarOn,
        lidarOpacity,
        entrancePinEnabled: entrancePinOn,
        vehiclePinsEnabled: vehiclePinsOn,
        performanceMode,
        centerLon: c.lng,
        centerLat: c.lat,
        zoom: map.getZoom(),
        bearing: map.getBearing(),
        pitch: map.getPitch(),
      };
      if (mapStateNearEnough(lastPersistedMapState, payload)) return;
      lastPersistedMapState = payload;
      postHost({ type: 'mapState', payload });
    }, 400);
  }

  function buildStatusMessage() {
    if (!hasEntrance()) {
      return 'No entrance coordinates ? set A1 in the app';
    }
    const parts = ['Entrance pin'];
    if (project.returnCar) parts.push('vehicle park');
    if (project.returnBase) parts.push('trailhead');
    if (project.surveyCorridor) parts.push('survey corridor');
    if (project.surfaceLidarRaster && project.surfaceLidarRaster.imageUrl) {
      parts.push(lidarOn ? 'Surface LiDAR loaded' : 'Surface LiDAR (hidden)');
    } else if (project.lidarStatusHint) {
      parts.push(project.lidarStatusHint);
    }
    return parts.join(' ? ');
  }

  function onProjectMessage(payload) {
    project = payload || {};
    projectReceived = true;
    if (hasEntrance()) delete project.emptyStateHint;
    const ms = project.mapState || {};
    performanceMode = !!ms.performanceMode;
    readLayerDefaults(ms);

    if (!map) {
      ensureMap();
      return;
    }

    addHillshade();
    addCopernicusDsm();
    addTerrain();
    applyEntrancePin();
    applyVehiclePins();
    applySurveyCorridor();
    applyLidarRaster();
    updateEmptyState();

    if (hasEntrance()) {
      const hasSavedView =
        Number.isFinite(ms.centerLon) &&
        Number.isFinite(ms.centerLat) &&
        Number.isFinite(ms.zoom) &&
        ms.zoom > 0;
      if (hasSavedView) {
        const c = map.getCenter();
        const payload = {
          centerLon: c.lng,
          centerLat: c.lat,
          zoom: map.getZoom(),
          bearing: map.getBearing(),
          pitch: map.getPitch(),
        };
        if (
          Math.abs(payload.centerLon - ms.centerLon) >= 1e-6
          || Math.abs(payload.centerLat - ms.centerLat) >= 1e-6
          || Math.abs(payload.zoom - ms.zoom) >= 1e-5
        ) {
          suppressPersist = true;
          map.jumpTo({
            center: [ms.centerLon, ms.centerLat],
            zoom: ms.zoom,
            bearing: ms.bearing || 0,
            pitch: ms.pitch || 0,
          });
          map.once('moveend', () => {
            suppressPersist = false;
          });
        }
      } else {
        fitEntrance();
      }
    } else {
      fitEntrance();
    }

    scheduleElevationProfile();
    scheduleMapResize();
    maybeWarnHeavyLayers();
    postHost({ type: 'status', message: buildStatusMessage() });
  }

  function onLayersMessage(payload) {
    if (payload && typeof payload.performanceMode === 'boolean') {
      performanceMode = payload.performanceMode;
      if (performanceMode) {
        hillshadeOn = false;
        terrain3dOn = false;
        corridorOn = false;
        copernicusOn = false;
        lidarOn = false;
      }
    }
    if (!performanceMode) {
      hillshadeOn = !!(payload && payload.hillshadeEnabled);
      terrain3dOn = !!(payload && payload.terrain3dEnabled);
      if (payload && typeof payload.corridorOverlayEnabled === 'boolean') {
        corridorOn = payload.corridorOverlayEnabled;
      }
      if (payload && typeof payload.copernicusDsmEnabled === 'boolean') {
        copernicusOn = payload.copernicusDsmEnabled;
      }
      if (payload && typeof payload.lidarOverlayEnabled === 'boolean') {
        lidarOn = payload.lidarOverlayEnabled;
      }
      if (payload && typeof payload.lidarOpacity === 'number') {
        setLidarOpacityValue(payload.lidarOpacity);
      }
      if (payload && typeof payload.entrancePinEnabled === 'boolean') {
        entrancePinOn = payload.entrancePinEnabled;
        applyEntrancePin();
      }
      if (payload && typeof payload.vehiclePinsEnabled === 'boolean') {
        vehiclePinsOn = payload.vehiclePinsEnabled;
        applyVehiclePins();
      }
    }
    if (!map) return;
    addHillshade();
    addCopernicusDsm();
    addTerrain();
    applySurveyCorridor();
    applyLidarRaster();
    schedulePersist();
    scheduleElevationProfile();
    maybeWarnHeavyLayers();
    postHost({ type: 'status', message: buildStatusMessage() });
  }

  function exportPng() {
    if (!map) {
      postHost({ type: 'exportPngResult', error: 'Map not ready' });
      return;
    }
    try {
      const dataUrl = map.getCanvas().toDataURL('image/png');
      postHost({ type: 'exportPngResult', dataUrl });
    } catch (err) {
      postHost({ type: 'exportPngResult', error: String(err && err.message ? err.message : err) });
    }
  }

  /* --- Elevation profile (Terrarium DEM, client-side) --- */

  function haversineM(lon1, lat1, lon2, lat2) {
    const R = 6378137;
    const toRad = Math.PI / 180;
    const dLat = (lat2 - lat1) * toRad;
    const dLon = (lon2 - lon1) * toRad;
    const a =
      Math.sin(dLat / 2) * Math.sin(dLat / 2) +
      Math.cos(lat1 * toRad) * Math.cos(lat2 * toRad) * Math.sin(dLon / 2) * Math.sin(dLon / 2);
    return 2 * R * Math.asin(Math.sqrt(a));
  }

  function corridorPathLengthM(coords) {
    if (!coords || coords.length < 2) return 0;
    let total = 0;
    for (let i = 1; i < coords.length; i++) {
      total += haversineM(coords[i - 1].lon, coords[i - 1].lat, coords[i].lon, coords[i].lat);
    }
    return total;
  }

  function collectCorridorCoords() {
    const corridor = project && project.surveyCorridor;
    if (!corridor || corridor.type !== 'FeatureCollection' || !Array.isArray(corridor.features)) return [];
    const out = [];
    corridor.features.forEach((f) => {
      if (!f || !f.geometry || f.geometry.type !== 'LineString') return;
      const coords = f.geometry.coordinates;
      if (!Array.isArray(coords) || coords.length < 2) return;
      coords.forEach((c) => {
        if (Array.isArray(c) && c.length >= 2 && isFinite(c[0]) && isFinite(c[1])) {
          out.push({ lon: c[0], lat: c[1] });
        }
      });
    });
    return out;
  }

  function resampleLine(points, targetCount) {
    const capped = Math.min(Math.max(2, targetCount | 0), MAX_ELEVATION_SAMPLES);
    if (!points || points.length === 0) return [];
    if (points.length <= capped) return points.slice();
    const segLens = [];
    let total = 0;
    for (let i = 1; i < points.length; i++) {
      const d = haversineM(points[i - 1].lon, points[i - 1].lat, points[i].lon, points[i].lat);
      segLens.push(d);
      total += d;
    }
    if (total < 1) return [points[0], points[points.length - 1]];
    const step = total / (capped - 1);
    const out = [points[0]];
    let segIdx = 0;
    let nextDist = step;
    let acc = 0;
    while (out.length < capped - 1 && segIdx < segLens.length) {
      const segEnd = acc + segLens[segIdx];
      if (nextDist <= segEnd) {
        const t = segLens[segIdx] > 0 ? (nextDist - acc) / segLens[segIdx] : 0;
        const a = points[segIdx];
        const b = points[segIdx + 1];
        out.push({
          lon: a.lon + (b.lon - a.lon) * t,
          lat: a.lat + (b.lat - a.lat) * t,
        });
        nextDist += step;
      } else {
        acc = segEnd;
        segIdx++;
      }
    }
    out.push(points[points.length - 1]);
    return out;
  }

  function lonLatToTile(lon, lat, zoom) {
    const n = Math.pow(2, zoom);
    const x = Math.floor(((lon + 180) / 360) * n);
    const latRad = (lat * Math.PI) / 180;
    const y = Math.floor((1 - Math.log(Math.tan(latRad) + 1 / Math.cos(latRad)) / Math.PI) / 2 * n);
    return { x, y, z: zoom };
  }

  function lonLatToPixel(lon, lat, tile) {
    const n = Math.pow(2, tile.z);
    const xf = ((lon + 180) / 360) * n;
    const latRad = (lat * Math.PI) / 180;
    const yf = (1 - Math.log(Math.tan(latRad) + 1 / Math.cos(latRad)) / Math.PI) / 2 * n;
    const px = Math.min(255, Math.max(0, Math.floor((xf - tile.x) * 256)));
    const py = Math.min(255, Math.max(0, Math.floor((yf - tile.y) * 256)));
    return { px, py };
  }

  function terrariumDecode(r, g, b) {
    return r * 256 + g + b / 256 - 32768;
  }

  const tileCache = new Map();

  function trimDemTileCache() {
    while (tileCache.size > DEM_TILE_CACHE_MAX) {
      const oldest = tileCache.keys().next().value;
      if (oldest == null) break;
      tileCache.delete(oldest);
    }
  }

  function fetchTerrariumTile(z, x, y) {
    const key = `${z}/${x}/${y}`;
    if (tileCache.has(key)) return tileCache.get(key);
    const url = DEM_TILES[0]
      .replace('{z}', String(z))
      .replace('{x}', String(x))
      .replace('{y}', String(y));
    const p = fetch(url, { mode: 'cors', credentials: 'omit' })
      .then((res) => {
        if (!res.ok) throw new Error('DEM tile ' + res.status);
        return res.blob();
      })
      .then((blob) => createImageBitmap(blob))
      .catch(() => null);
    tileCache.set(key, p);
    trimDemTileCache();
    return p;
  }

  async function elevationsForTile(tile, points) {
    const bmp = await fetchTerrariumTile(tile.z, tile.x, tile.y);
    if (!bmp) return points.map(() => null);
    const canvas = document.createElement('canvas');
    canvas.width = 256;
    canvas.height = 256;
    const ctx = canvas.getContext('2d', { willReadFrequently: true });
    ctx.drawImage(bmp, 0, 0);
    return points.map((pt) => {
      const { px, py } = lonLatToPixel(pt.lon, pt.lat, tile);
      const data = ctx.getImageData(px, py, 1, 1).data;
      return terrariumDecode(data[0], data[1], data[2]);
    });
  }

  async function sampleElevationsBatch(samples, zoom) {
    const byTile = new Map();
    samples.forEach((pt, index) => {
      const tile = lonLatToTile(pt.lon, pt.lat, zoom);
      const key = `${tile.z}/${tile.x}/${tile.y}`;
      if (!byTile.has(key)) byTile.set(key, { tile, points: [] });
      byTile.get(key).points.push({ index, lon: pt.lon, lat: pt.lat });
    });
    const elevations = new Array(samples.length).fill(null);
    const groups = Array.from(byTile.values());
    for (let i = 0; i < groups.length; i += ELEVATION_TILE_CONCURRENCY) {
      const chunk = groups.slice(i, i + ELEVATION_TILE_CONCURRENCY);
      await Promise.all(chunk.map(async ({ tile, points }) => {
        const vals = await elevationsForTile(tile, points);
        points.forEach((pt, j) => { elevations[pt.index] = vals[j]; });
      }));
    }
    return elevations;
  }

  function drawElevationChart(distancesM, elevationsM) {
    const panel = document.getElementById('elevation-panel');
    const canvas = document.getElementById('elevation-chart');
    const status = document.getElementById('elevation-status');
    if (!panel || !canvas) return;
    panel.classList.remove('hidden');
    document.body.classList.add('has-elevation');

    const ctx = canvas.getContext('2d');
    const w = canvas.width;
    const h = canvas.height;
    ctx.clearRect(0, 0, w, h);
    ctx.fillStyle = '#010409';
    ctx.fillRect(0, 0, w, h);

    const valid = elevationsM.filter((e) => e != null && isFinite(e));
    const maxD = distancesM[distancesM.length - 1] || 0;
    if (maxD < 10) {
      if (status) status.textContent = 'No corridor length along path (need at least 10 m).';
      return;
    }
    if (valid.length < 2) {
      if (status) status.textContent = 'Could not sample elevation (check internet).';
      return;
    }

    const minE = Math.min.apply(null, valid);
    const maxE = Math.max.apply(null, valid);
    const padE = Math.max(5, (maxE - minE) * 0.1);
    const e0 = minE - padE;
    const e1 = maxE + padE;

    ctx.strokeStyle = '#30363d';
    ctx.lineWidth = 1;
    ctx.beginPath();
    ctx.moveTo(8, h - 12);
    ctx.lineTo(w - 8, h - 12);
    ctx.stroke();

    ctx.strokeStyle = '#00d4aa';
    ctx.lineWidth = 2;
    ctx.beginPath();
    let started = false;
    for (let i = 0; i < elevationsM.length; i++) {
      const e = elevationsM[i];
      if (e == null || !isFinite(e)) continue;
      const x = 8 + ((distancesM[i] / maxD) * (w - 16));
      const y = h - 12 - ((e - e0) / (e1 - e0)) * (h - 24);
      if (!started) {
        ctx.moveTo(x, y);
        started = true;
      } else {
        ctx.lineTo(x, y);
      }
    }
    ctx.stroke();

    if (status) {
      status.textContent =
        `Distance ${(maxD / 1000).toFixed(2)} km ? elevation ${minE.toFixed(0)}?${maxE.toFixed(0)} m`;
    }
  }

  function hideElevationPanel() {
    const panel = document.getElementById('elevation-panel');
    if (panel) panel.classList.add('hidden');
    document.body.classList.remove('has-elevation');
  }

  let elevationTimer = null;
  function scheduleElevationProfile() {
    if (performanceMode || mapMoving) return;
    clearTimeout(elevationTimer);
    const run = () => { elevationTimer = setTimeout(runElevationProfile, ELEVATION_DEBOUNCE_MS); };
    if (typeof requestIdleCallback === 'function') requestIdleCallback(run, { timeout: ELEVATION_DEBOUNCE_MS + 400 });
    else run();
  }

  function elevationSampleCount(pathLenM) {
    if (pathLenM < 10) return 0;
    const byLength = Math.min(MAX_ELEVATION_SAMPLES, Math.max(8, Math.ceil(pathLenM / 250)));
    return Math.min(byLength, MAX_ELEVATION_SAMPLES);
  }

  async function runElevationProfile() {
    if (elevationBusy || performanceMode || mapMoving) return;
    const coords = collectCorridorCoords();
    const pathLenM = corridorPathLengthM(coords);
    if (!corridorOn || coords.length < 2 || pathLenM < 10 || !navigator.onLine) {
      hideElevationPanel();
      return;
    }
    const sampleCount = elevationSampleCount(pathLenM);
    if (sampleCount < 2) { hideElevationPanel(); return; }
    const runId = ++elevationRunId;
    elevationBusy = true;
    setMapBusy(true, 'Sampling elevation…');
    try {
      const samples = resampleLine(coords, sampleCount);
      const zoom = Math.min(12, Math.max(8, Math.round(map ? map.getZoom() : 12)));
      const elevations = await sampleElevationsBatch(samples, zoom);
      if (runId !== elevationRunId || mapMoving) return;
      const distances = [0];
      for (let i = 1; i < samples.length; i += 1) {
        distances.push(distances[i - 1] + haversineM(samples[i - 1].lon, samples[i - 1].lat, samples[i].lon, samples[i].lat));
      }
      drawElevationChart(distances, elevations);
      postHost({ type: 'elevationProfile', ready: elevations.filter((x) => x != null).length >= 2 });
    } catch {
      hideElevationPanel();
    } finally {
      elevationBusy = false;
      setMapBusy(false);
    }
  }

  function handleHostMessage(data) {
    if (!data || !data.type) return;
    switch (data.type) {
      case 'project':
        onProjectMessage(data.payload);
        break;
      case 'layers':
        onLayersMessage(data.payload);
        break;
      case 'fitEntrance':
        fitEntrance();
        break;
      case 'fitSurvey':
        fitSurvey();
        break;
      case 'measure':
        setMeasureMode(!!(data.payload && data.payload.active));
        break;
      case 'exportPackage':
        exportPackage();
        break;
      case 'getCoords': {
        const source = data.payload && data.payload.source;
        const pt = resolveCoordsRequest(source);
        if (pt) {
          postHost({
            type: 'coordsCopy',
            source: source || 'cursor',
            lat: pt.lat,
            lon: pt.lon,
            text: pt.lat.toFixed(6) + ', ' + pt.lon.toFixed(6),
          });
        } else {
          postHost({ type: 'coordsCopy', error: 'No coordinates for ' + (source || 'cursor') });
        }
        break;
      }
      case 'lidarOpacity':
        if (data.payload && typeof data.payload.opacity === 'number') {
          setLidarOpacityValue(data.payload.opacity);
        }
        break;
      case 'pinPick': {
        const kind = data.payload && data.payload.kind;
        if (kind === 'vehicle' || kind === 'base') {
          pinPickKind = kind;
          measureOn = false;
          measurePoints = [];
          updateMeasureOverlay();
          if (map) map.getCanvas().style.cursor = 'crosshair';
          postHost({ type: 'pinPickMode', active: true, kind });
          postHost({ type: 'status', message: 'Click map to set ' + (kind === 'vehicle' ? 'vehicle park' : 'trailhead') + ' pin' });
        } else {
          pinPickKind = null;
          if (map) map.getCanvas().style.cursor = measureOn ? 'crosshair' : '';
          postHost({ type: 'pinPickMode', active: false, kind: null });
        }
        break;
      }
      case 'pinPickGps':
        if (data.payload && (data.payload.kind === 'vehicle' || data.payload.kind === 'base')) {
          placePinFromGps(data.payload.kind);
        }
        break;
      case 'resize':
        scheduleMapResize();
        break;
      case 'exportPng':
        exportPng();
        break;
      default:
        break;
    }
  }

  if (window.chrome && window.chrome.webview) {
    window.chrome.webview.addEventListener('message', (ev) => {
      let data = ev.data;
      if (typeof data === 'string') {
        try { data = JSON.parse(data); } catch { return; }
      }
      handleHostMessage(data);
    });
  }

  window.addEventListener('message', (ev) => {
    if (ev.origin !== window.location.origin) return;
    let data = ev.data;
    if (typeof data === 'string') {
      try { data = JSON.parse(data); } catch { return; }
    }
    handleHostMessage(data);
  });

  ensureMap();
})();
