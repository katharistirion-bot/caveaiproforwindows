"use strict";

/** Basic per-user rate limit stored in Firestore (survives cold starts). */
const WINDOW_MS = 60 * 60 * 1000;
const MAX_REQUESTS_PER_WINDOW = 20;

/**
 * @param {FirebaseFirestore.Firestore} db
 * @param {string} uid
 */
async function assertWithinRateLimit(db, uid) {
  const ref = db.collection("replicate_proxy_rate").doc(uid);
  const now = Date.now();

  await db.runTransaction(async (tx) => {
    const snap = await tx.get(ref);
    const data = snap.exists ? snap.data() : null;
    let windowStartMs = data?.windowStartMs ?? now;
    let requestCount = data?.requestCount ?? 0;

    if (now - windowStartMs >= WINDOW_MS) {
      windowStartMs = now;
      requestCount = 0;
    }

    if (requestCount >= MAX_REQUESTS_PER_WINDOW) {
      const retryAfterMs = Math.max(0, WINDOW_MS - (now - windowStartMs));
      const err = new Error(
        `Rate limit exceeded (${MAX_REQUESTS_PER_WINDOW} generative renders per hour). Try again later.`,
      );
      err.code = "resource-exhausted";
      err.details = { retryAfterMs, maxPerWindow: MAX_REQUESTS_PER_WINDOW };
      throw err;
    }

    tx.set(ref, {
      windowStartMs,
      requestCount: requestCount + 1,
      updatedAtMs: now,
    });
  });
}

module.exports = {
  MAX_REQUESTS_PER_WINDOW,
  WINDOW_MS,
  assertWithinRateLimit,
};
