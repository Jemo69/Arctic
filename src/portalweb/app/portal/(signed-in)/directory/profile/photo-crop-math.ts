/** The crop square on screen, and the side of the square image that gets uploaded. */
export const VIEW = 300;
export const OUT = 600;

/** Keeps the image covering the square: x and y are the image's top-left in view pixels, never past an edge. */
export const clamp = (x: number, y: number, w: number, h: number) => ({ x: Math.min(0, Math.max(VIEW - w, x)), y: Math.min(0, Math.max(VIEW - h, y)) });

/** Which square of the original photo the view shows, in the photo's own pixels. */
export const cropRect = (pos: { x: number; y: number }, scale: number) => ({ sx: -pos.x / scale, sy: -pos.y / scale, size: VIEW / scale });
