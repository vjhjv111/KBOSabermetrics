export type StadiumTimeOfDay = 'day' | 'night';

const HOUR = 3_600_000;
const DAY = 24 * HOUR;
// Korea uses UTC+9 year-round. UTC arithmetic is independent of the device's timezone.
const koreaDayTime = (epochMs: number) => ((epochMs + 9 * HOUR) % DAY + DAY) % DAY;

export function koreaTimeOfDay(epochMs = Date.now()): StadiumTimeOfDay {
 const time = koreaDayTime(epochMs);
 return time >= 6 * HOUR && time < 18 * HOUR ? 'day' : 'night';
}

export function millisecondsUntilKoreaTimeChange(epochMs = Date.now()): number {
 const time = koreaDayTime(epochMs);
 return time < 6 * HOUR ? 6 * HOUR - time : time < 18 * HOUR ? 18 * HOUR - time : DAY + 6 * HOUR - time;
}

/** Follow real wall time, independently of pitch clocks, playback speed and match state. */
export function watchKoreaTimeOfDay(onChange: (phase: StadiumTimeOfDay) => void): () => void {
 let timer: ReturnType<typeof setTimeout> | undefined;
 let phase: StadiumTimeOfDay | undefined;
 let stopped = false;
 const check = () => {
  if (stopped) return;
  clearTimeout(timer);
  const now = Date.now(), next = koreaTimeOfDay(now);
  if (next !== phase) { phase = next; onChange(next); }
  // Recheck at the boundary, and at least once a minute after a system clock change.
  timer = setTimeout(check, Math.min(60_000, millisecondsUntilKoreaTimeChange(now)));
 };
 check();
 if (typeof document !== 'undefined') document.addEventListener('visibilitychange', check);
 if (typeof window !== 'undefined') window.addEventListener('focus', check);
 return () => {
  stopped = true;
  clearTimeout(timer);
  if (typeof document !== 'undefined') document.removeEventListener('visibilitychange', check);
  if (typeof window !== 'undefined') window.removeEventListener('focus', check);
 };
}
