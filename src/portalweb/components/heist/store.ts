/**
 * What the code rain is showing, shared between the rain and the signal button.
 *
 * A plain module object rather than React state: the rain draws on a canvas
 * twenty times a second and never re-renders, and the signal panel only needs
 * to read the message at the moment somebody opens it.
 */
export const transmission = {
  paused: false,
  /** The message on screen right now, or "" between messages. */
  current: "",
  /** The last message that finished, so pausing between two still has one. */
  last: "",
};

/**
 * The lines the rain spells out. Role words nod at finding teammates; the
 * phrases are the "intercepted" ones. One cycle in eight carries the
 * clearance code.
 */
export const MESSAGES = [
  "THE CREW IS THE KEY",
  "DESIGNER",
  "TRUST YOUR TEAM",
  "BACKEND",
  "WHO'S ON YOUR SIDE?",
  "PITCH",
  "FRONTEND",
  "CODE: ONEJOB",
];
