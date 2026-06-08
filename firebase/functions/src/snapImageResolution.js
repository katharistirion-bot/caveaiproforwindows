"use strict";

/** Replicate jagilley/controlnet-scribble accepts only these string enum values. */
const VALID_IMAGE_RESOLUTIONS = [256, 512, 768];

/**
 * Maps an arbitrary resolution (number or numeric string) to the nearest allowed
 * Replicate image_resolution enum. Ties prefer the higher resolution.
 *
 * @param {unknown} value
 * @returns {"256" | "512" | "768"}
 */
function snapImageResolution(value) {
  const numeric = parseResolutionNumber(value);
  if (numeric == null) {
    return "512";
  }

  let best = VALID_IMAGE_RESOLUTIONS[0];
  let bestDistance = Math.abs(numeric - best);

  for (const candidate of VALID_IMAGE_RESOLUTIONS) {
    const distance = Math.abs(numeric - candidate);
    if (distance < bestDistance || (distance === bestDistance && candidate > best)) {
      best = candidate;
      bestDistance = distance;
    }
  }

  return String(best);
}

/**
 * @param {Record<string, unknown>} input
 * @returns {Record<string, unknown>}
 */
function normalizeReplicateInput(input) {
  if (!input || typeof input !== "object" || Array.isArray(input)) {
    return input;
  }

  if (!Object.prototype.hasOwnProperty.call(input, "image_resolution")) {
    return input;
  }

  return {
    ...input,
    image_resolution: snapImageResolution(input.image_resolution),
  };
}

/**
 * @param {unknown} value
 * @returns {number | null}
 */
function parseResolutionNumber(value) {
  if (typeof value === "number" && Number.isFinite(value)) {
    return value;
  }

  if (typeof value === "string") {
    const trimmed = value.trim();
    if (!trimmed) return null;
    const parsed = Number(trimmed);
    return Number.isFinite(parsed) ? parsed : null;
  }

  return null;
}

module.exports = {
  VALID_IMAGE_RESOLUTIONS,
  snapImageResolution,
  normalizeReplicateInput,
};
