/**
 * A hand-drawn marker loop, the kind you draw around one item on a planning
 * board. Drawn around the current nav tab in the Heist theme; it draws itself
 * in when it mounts, so moving to another tab circles the new one.
 *
 * The shape is generated rather than an asset so every tab gets its own loop:
 * a wobbly ellipse that runs a little past a full turn, so the ends overlap
 * the way a pen stroke does, plus a fainter second pass for marker texture.
 * Seeded from the tab, so a tab's loop is the same on every visit.
 */

const W = 100;
const H = 40;

function random(seed: number) {
  // mulberry32: tiny, seedable, good enough for a wobble.
  return () => {
    seed |= 0;
    seed = (seed + 0x6d2b79f5) | 0;
    let t = Math.imul(seed ^ (seed >>> 15), 1 | seed);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

function hash(text: string) {
  let h = 2166136261;
  for (let i = 0; i < text.length; i++) h = Math.imul(h ^ text.charCodeAt(i), 16777619);
  return h >>> 0;
}

function loop(seed: number) {
  const rand = random(seed);
  const start = -2.5 + rand() * 0.6; // begin upper left, as a right hand would
  const turns = 1.1 + rand() * 0.12;
  const wobble = [rand() * 6, rand() * 6, 0.03 + rand() * 0.03, 0.02 + rand() * 0.02];
  const steps = 56;
  let d = "";
  for (let i = 0; i <= steps; i++) {
    const p = i / steps;
    const a = start + p * turns * Math.PI * 2;
    // Slow wobble around the ellipse, and a drift so the closing stroke lands
    // just outside where it started.
    const r = 1 + wobble[2] * Math.sin(2 * a + wobble[0]) + wobble[3] * Math.sin(3 * a + wobble[1]) + p * 0.05;
    const x = W / 2 + (W / 2 - 3) * r * Math.cos(a);
    const y = H / 2 + (H / 2 - 3) * r * Math.sin(a) - p * 2;
    d += `${i ? "L" : "M"}${x.toFixed(2)} ${y.toFixed(2)}`;
  }
  return d;
}

export function MarkerLoop({ id }: { id: string }) {
  const seed = hash(id);
  return (
    <svg className="heist-marker" viewBox={`0 0 ${W} ${H}`} preserveAspectRatio="none" aria-hidden="true">
      <path d={loop(seed)} pathLength={1} />
      <path d={loop(seed + 7)} pathLength={1} className="heist-marker__ghost" />
    </svg>
  );
}
