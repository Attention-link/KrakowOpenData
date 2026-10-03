// Outbox: reports written while offline are kept on the device and sent when the connection is back.

import { useEffect, useState } from 'react';
import { kvGet, kvSet } from './storage';
import { isOffline } from './store';
import { ApiError, postReport } from './api';
import { t } from './i18n';
import { toast } from '../components/ui';

interface OutboxItem { kind: 'report'; body: unknown; queuedAt: number }

export async function outboxList(): Promise<OutboxItem[]> { return (await kvGet<OutboxItem[]>('outbox')) || []; }

const listeners = new Set<(n: number) => void>();
const emit = (n: number) => listeners.forEach((fn) => fn(n));

export async function outboxAdd(item: { kind: 'report'; body: unknown }) {
  const list = await outboxList();
  list.push({ ...item, queuedAt: Date.now() });
  await kvSet('outbox', list);
  emit(list.length);
  return list.length;
}

let flushing = false;

/** Sends queued reports. Items the server rejects for good (400/429) are dropped with a notice. */
export async function flushOutbox(): Promise<number> {
  if (flushing || isOffline()) return 0;
  flushing = true;
  let sent = 0;
  try {
    const list = await outboxList();
    const keep: OutboxItem[] = [];
    for (const item of list) {
      try {
        await postReport(item.body);
        sent++;
      } catch (e) {
        if (e instanceof ApiError && e.status >= 400 && e.status < 500) toast(t('outbox.dropped'), { error: true });
        else keep.push(item);
      }
    }
    await kvSet('outbox', keep);
    emit(keep.length);
    if (sent) toast(t('outbox.sent', { n: sent }));
  } finally {
    flushing = false;
  }
  return sent;
}

/** How many reports are waiting to be sent. */
export function useOutboxCount(): number {
  const [n, setN] = useState(0);
  useEffect(() => {
    void outboxList().then((l) => setN(l.length));
    listeners.add(setN);
    return () => { listeners.delete(setN); };
  }, []);
  return n;
}
