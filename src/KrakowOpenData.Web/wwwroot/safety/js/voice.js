// "Record by voice" for the report note: for people who find typing hard. Records up to 60 s with MediaRecorder, sends the
// audio to the API (which passes it to the AI Worker; the key stays on the server) and returns the text and the AI-suggested
// category, which the resident can still change. The recording is never stored. Hidden when the browser cannot record.

import { h } from './util.js';
import { t } from './i18n.js';
import { isOffline } from './state.js';
import { postVoice, ApiError } from './api.js';

const MAX_SECONDS = 60;
const MAX_BYTES = 1024 * 1024;

export const voiceSupported = () => typeof window.MediaRecorder === 'function' && !!navigator.mediaDevices?.getUserMedia;

function pickType() {
  for (const type of ['audio/webm;codecs=opus', 'audio/webm', 'audio/ogg;codecs=opus', 'audio/mp4']) {
    try { if (MediaRecorder.isTypeSupported(type)) return type; } catch { /* old browser */ }
  }
  return '';
}

/**
 * A record / stop button with a live status line (role="status"). onResult({ text, suggestedType }) is called with the transcript.
 * Returns null when recording is not possible here, so callers can simply leave it out.
 */
export function voiceButton({ onResult }) {
  if (!voiceSupported()) return null;
  const status = h('p', { class: 'small muted', role: 'status', 'aria-live': 'polite' }, t('voice.hint'));
  const btn = h('button', { class: 'btn', type: 'button', 'aria-pressed': 'false' }, t('voice.record'));
  let recorder = null, stream = null, timer = null, left = MAX_SECONDS, chunks = [];

  const label = (text) => { btn.textContent = text; };
  const say = (text, error = false) => { status.textContent = text; status.classList.toggle('err', error); };

  function stopTracks() {
    stream?.getTracks().forEach((tr) => tr.stop());
    stream = null;
    clearInterval(timer);
    timer = null;
  }

  async function start() {
    if (isOffline()) { say(t('voice.failed'), true); return; }
    try {
      stream = await navigator.mediaDevices.getUserMedia({ audio: true });
    } catch {
      say(t('voice.denied'), true);
      return;
    }
    const type = pickType();
    chunks = [];
    recorder = new MediaRecorder(stream, type ? { mimeType: type, audioBitsPerSecond: 64000 } : undefined);
    recorder.addEventListener('dataavailable', (e) => { if (e.data?.size) chunks.push(e.data); });
    recorder.addEventListener('stop', finish);
    recorder.start();
    left = MAX_SECONDS;
    btn.setAttribute('aria-pressed', 'true');
    label(t('voice.stop', { s: left }));
    say(t('voice.recording'));
    timer = setInterval(() => {
      left -= 1;
      label(t('voice.stop', { s: Math.max(0, left) }));
      if (left <= 0) recorder?.state === 'recording' && recorder.stop();
    }, 1000);
  }

  async function finish() {
    stopTracks();
    btn.setAttribute('aria-pressed', 'false');
    label(t('voice.record'));
    const blob = new Blob(chunks, { type: (recorder?.mimeType || chunks[0]?.type || 'audio/webm').split(';')[0] });
    recorder = null;
    chunks = [];
    if (!blob.size) { say(t('voice.failed'), true); return; }
    if (blob.size > MAX_BYTES) { say(t('voice.tooLong'), true); return; }
    btn.disabled = true;
    say(t('voice.working'));
    try {
      const r = await postVoice(blob);
      onResult({ text: r.text || '', suggestedType: r.suggestedType || null });
      say(r.suggestedType ? `${t('voice.done')} ${t('voice.suggested', { type: t(`rtype.${r.suggestedType}`) })}` : t('voice.done'));
    } catch (e) {
      say(e instanceof ApiError && e.status === 429 ? t('voice.rate') : t('voice.failed'), true);
    } finally {
      btn.disabled = false;
    }
  }

  btn.addEventListener('click', () => {
    if (recorder?.state === 'recording') recorder.stop();
    else start();
  });

  return h('div', { class: 'stack tight' }, h('div', { class: 'row wrap' }, btn), status);
}

const info = (rs) => (rs.voiceInfo ? h('p', { class: 'banner small info', role: 'status' }, rs.voiceInfo) : null);

/**
 * Step 1 of the report form (choose what you noticed): a recording fills the note and preselects the AI-suggested category when
 * it belongs to this view, then moves on to step 2. Returns nodes for Element.append (no nulls).
 */
export function voiceStep1(rs, types, paint) {
  const btn = voiceButton({
    onResult: ({ text, suggestedType }) => {
      rs.note = (text || '').slice(0, 200);
      const fits = !!suggestedType && types.some((x) => x.type === suggestedType);
      if (fits) rs.type = suggestedType;
      rs.voiceInfo = `${t('voice.done')}${fits ? ' ' + t('voice.suggested', { type: t(`rtype.${suggestedType}`) }) : ' ' + t('voice.pickType')}`;
      if (fits) rs.step = 2;
      paint();
    }
  });
  return [btn, info(rs)].filter(Boolean);
}

/** Step 2 (the note): a recording replaces the note text in place, without repainting the form. */
export function voiceStep2(rs, noteInput, count) {
  const btn = voiceButton({
    onResult: ({ text }) => {
      rs.note = (text || '').slice(0, 200);
      noteInput.value = rs.note;
      count.textContent = `${rs.note.length}/200`;
    }
  });
  return [info(rs), btn].filter(Boolean);
}
