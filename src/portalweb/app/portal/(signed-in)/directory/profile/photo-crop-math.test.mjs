import assert from "node:assert/strict";
import test from "node:test";
import { VIEW, clamp, cropRect } from "./photo-crop-math.ts";

test("a 900x600 photo at zoom 2, pushed to the right edge, crops its right-hand square", () => {
  const scale = (VIEW / 600) * 2; // cover scale for the short side, times zoom
  const pos = clamp(-10_000, -150, 900 * scale, 600 * scale);
  assert.deepEqual(pos, { x: -600, y: -150 });
  assert.deepEqual(cropRect(pos, scale), { sx: 600, sy: 150, size: 300 });
});

test("the photo can never be dragged off an edge of the square", () => {
  assert.deepEqual(clamp(50, 50, 450, 300), { x: 0, y: 0 });
  assert.deepEqual(clamp(-500, -500, 450, 300), { x: -150, y: 0 });
});
