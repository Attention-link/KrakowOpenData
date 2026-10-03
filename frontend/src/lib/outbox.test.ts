import { beforeEach, describe, expect, it, vi } from 'vitest';

const postReport = vi.fn();
vi.mock('./api', async (orig) => ({ ...(await orig<typeof import('./api')>()), postReport: (...a: unknown[]) => postReport(...a) }));

import { ApiError, NetworkError } from './api';
import { flushOutbox, outboxAdd, outboxList } from './outbox';
import { kvClearAll } from './storage';
import { setApp } from './store';

beforeEach(async () => { postReport.mockReset(); await kvClearAll(); setApp({ online: true, apiOk: true }); });

describe('outbox', () => {
  it('keeps reports written offline and counts them', async () => {
    expect(await outboxAdd({ kind: 'report', body: { type: 'LightOut' } })).toBe(1);
    expect(await outboxAdd({ kind: 'report', body: { type: 'NoShade' } })).toBe(2);
    expect(await outboxList()).toHaveLength(2);
  });

  it('sends everything when the connection is back and empties the outbox', async () => {
    postReport.mockResolvedValue({});
    await outboxAdd({ kind: 'report', body: { type: 'LightOut' } });
    await outboxAdd({ kind: 'report', body: { type: 'NoShade' } });
    expect(await flushOutbox()).toBe(2);
    expect(postReport).toHaveBeenCalledTimes(2);
    expect(await outboxList()).toHaveLength(0);
  });

  it('does not try while offline', async () => {
    await outboxAdd({ kind: 'report', body: {} });
    setApp({ apiOk: false });
    expect(await flushOutbox()).toBe(0);
    expect(postReport).not.toHaveBeenCalled();
    expect(await outboxList()).toHaveLength(1);
  });

  it('keeps a report that failed for a network reason, to try again later', async () => {
    postReport.mockRejectedValue(new NetworkError('network'));
    await outboxAdd({ kind: 'report', body: {} });
    expect(await flushOutbox()).toBe(0);
    expect(await outboxList()).toHaveLength(1);
  });

  it('drops a report the server refuses for good (it would never be accepted)', async () => {
    postReport.mockRejectedValue(new ApiError(400, { title: 'bad' }));
    await outboxAdd({ kind: 'report', body: {} });
    await flushOutbox();
    expect(await outboxList()).toHaveLength(0);
  });

  it('keeps the good reports when one of several fails', async () => {
    postReport.mockResolvedValueOnce({}).mockRejectedValueOnce(new ApiError(500, null)).mockResolvedValueOnce({});
    for (let i = 0; i < 3; i++) await outboxAdd({ kind: 'report', body: { i } });
    expect(await flushOutbox()).toBe(2);
    expect((await outboxList()).map((x) => (x.body as any).i)).toEqual([1]);
  });
});
