import {fromVideo} from './marker.js';
const el = id => document.getElementById(id);
const video = el('player'), source = el('source');
let hls, frames, selected, requestId = 0, momentId = 0;
async function recordings() {
  const response = await fetch('api/segments');
  if (!response.ok) throw Error('Could not load recordings.');
  const keep = source.value, rows = await response.json();
  source.replaceChildren(new Option('Live broadcast', ''));
  for (const r of rows) source.add(new Option(r.session + ' · ' + r.lo.toFixed(1) + '–' + r.hi.toFixed(1) + ' s', r.name));
  if (keep && !rows.some(r => r.name === keep)) source.add(new Option('Current recording', keep));
  source.value = keep;
}
async function playSource() {
  const id = ++requestId;
  if (hls) hls.destroy();
  hls = frames = null;
  selected = source.value;
  video.pause(); video.removeAttribute('src'); video.load();
  el('status').textContent = '';
  if (selected) {
    const base = 'api/media/' + encodeURIComponent(selected);
    const res = await fetch(base + '/mapping');
    if (!res.ok) throw Error('This recording is unavailable.');
    const mapping = await res.json();
    if (id !== requestId) return;
    frames = mapping;
    video.src = base + '/video';
  } else if (video.canPlayType('application/vnd.apple.mpegurl')) {
    video.src = '/hls/stream.m3u8';
  } else if (window.Hls?.isSupported()) {
    hls = new Hls();
    hls.on(Hls.Events.ERROR, (_, data) => {
      if (data.fatal) el('status').textContent = 'Live playback unavailable. Select a recording or reload the live broadcast.';
    });
    hls.loadSource('/hls/stream.m3u8'); hls.attachMedia(video);
  } else throw Error('HLS playback is unavailable in this browser.');
}
function showMoment(ref) {
  momentId++;
  const query = new URLSearchParams({session: ref.session, time: ref.time.toFixed(3)});
  history.replaceState(null, '', '#' + query);
  el('incident').hidden = false;
  el('reference').textContent = ref.session + ' · ' + ref.time.toFixed(3) + ' s';
  el('download').href = 'api/report?' + query;
  el('permalink').href = location.href;
  el('availability').textContent = '';
}
function report() {
  try {
    if (video.seeking || video.readyState < 2) throw Error('Wait for the selected picture to load.');
    const ref = selected ? frames?.[Math.min(frames.length-1, Math.floor(video.currentTime*12))] : fromVideo(video);
    if (!ref) throw Error('No readable capture reference in this frame. Enable the stream marker or select a recorded segment.');
    showMoment(ref);
    el('status').textContent = 'Moment selected.';
  } catch (err) { el('status').textContent = err.message; }
}
el('report').onclick = report;
el('download').onclick = async event => {
  event.preventDefault();
  const id = momentId;
  el('availability').textContent = 'Preparing evidence…';
  try {
    const res = await fetch(el('download').href);
    if (id !== momentId) return;
    if (!res.ok) throw Error(res.status === 404 ? 'Evidence has not arrived or has expired. Retry this moment after the segment closes.' : 'Evidence download failed (' + res.status + ').');
    const blob = await res.blob();
    if (id !== momentId) return;
    const a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = 'review-' + new URL(el('download').href).searchParams.get('session') + '-' + new URL(el('download').href).searchParams.get('time') + '.zip';
    a.click(); setTimeout(() => URL.revokeObjectURL(a.href), 60000);
    el('availability').textContent = res.headers.get('X-Trace-Records') + ' trace records · ' +
      res.headers.get('X-Video-Segments') + ' completed segments. For a recent incident, retry this same download after the next segment closes.';
  } catch (err) { if (id === momentId) el('availability').textContent = err.message; }
};
function error(err) { el('status').textContent = err.message; }
el('refresh').onclick = () => recordings().catch(error);
source.onchange = () => playSource().catch(error);
const params = new URLSearchParams(location.hash.slice(1)), time = Number(params.get('time'));
if (/^\d{8}T\d{9}-[0-9a-f]{8}$/.test(params.get('session')) && params.has('time') && Number.isFinite(time) && time >= 0)
  showMoment({session: params.get('session'), time});
recordings().catch(error); playSource().catch(error);
