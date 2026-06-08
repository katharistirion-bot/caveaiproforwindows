"use strict";

/**
 * Mirrors C# SubscriptionEntitlementPolicy + SubscriptionEntitlementService.
 * Validates user_entitlements/{uid} and app_install_grace/{graceDocId}.
 */

const INSTALL_GRACE_PERIOD_DAYS = 30;
const INSTALL_GRACE_PERIOD_MS = INSTALL_GRACE_PERIOD_DAYS * 24 * 60 * 60 * 1000;
const GRACE_DOC_ID_PATTERN = /^[0-9a-f]{64}$/;

function isAllowedStatus(status) {
  if (!status) return false;
  return status === "ACTIVE" || status.toUpperCase() === "TRIALING";
}

function isValidGraceDocId(graceDocId) {
  return typeof graceDocId === "string" && GRACE_DOC_ID_PATTERN.test(graceDocId.trim());
}

function isInstallGraceWindowOpen(firstSeenMsUtc, nowMs = Date.now()) {
  if (!firstSeenMsUtc || firstSeenMsUtc <= 0) return false;
  return nowMs < firstSeenMsUtc + INSTALL_GRACE_PERIOD_MS;
}

function premiumUntilMs(data) {
  const ts = data?.premiumCloudUntil;
  if (!ts) return null;
  if (typeof ts.toMillis === "function") return ts.toMillis();
  if (typeof ts === "number") return ts;
  return null;
}

/**
 * @param {FirebaseFirestore.Firestore} db
 * @param {string} uid
 * @returns {Promise<{ accessKind: string }>}
 */
async function assertPremiumEntitlement(db, uid) {
  const snap = await db.collection("user_entitlements").doc(uid).get();
  if (!snap.exists) {
    const err = new Error("No subscription profile found for this account.");
    err.code = "permission-denied";
    throw err;
  }

  const data = snap.data() || {};
  const status = data.status || "";
  if (!isAllowedStatus(status)) {
    const err = new Error(
      `Subscription status is '${status || "unknown"}' (expected ACTIVE or TRIALING).`,
    );
    err.code = "permission-denied";
    throw err;
  }

  const untilMs = premiumUntilMs(data);
  if (untilMs == null) {
    const err = new Error("Subscription expiry (premiumCloudUntil) is missing.");
    err.code = "permission-denied";
    throw err;
  }
  if (untilMs <= Date.now()) {
    const err = new Error("Subscription or trial period has expired.");
    err.code = "permission-denied";
    throw err;
  }

  const source = data.entitlementSource || "";

  if (source === "PLAY_SUBSCRIPTION") {
    return { accessKind: "PaidPlaySubscription" };
  }

  if (source === "INSTALL_GRACE") {
    const graceDocId = data.graceDocId;
    if (!isValidGraceDocId(graceDocId)) {
      const err = new Error("Install-grace trial is missing a valid graceDocId.");
      err.code = "permission-denied";
      throw err;
    }

    const graceSnap = await db.collection("app_install_grace").doc(graceDocId.trim()).get();
    if (!graceSnap.exists) {
      const err = new Error("Install-grace trial record was not found on the server.");
      err.code = "permission-denied";
      throw err;
    }

    const firstSeenMs = graceSnap.data()?.firstSeenMs;
    const firstSeenMsUtc =
      typeof firstSeenMs === "number"
        ? firstSeenMs
        : typeof firstSeenMs?.toNumber === "function"
          ? firstSeenMs.toNumber()
          : null;

    if (!isInstallGraceWindowOpen(firstSeenMsUtc)) {
      const err = new Error(
        `Install-grace trial ended (>${INSTALL_GRACE_PERIOD_DAYS}-day window).`,
      );
      err.code = "permission-denied";
      throw err;
    }

    return { accessKind: "InstallGraceTrial" };
  }

  if (!source) {
    return { accessKind: "LegacyEntitlement" };
  }

  const err = new Error(`Unknown entitlementSource '${source}'.`);
  err.code = "permission-denied";
  throw err;
}

module.exports = {
  INSTALL_GRACE_PERIOD_DAYS,
  assertPremiumEntitlement,
  isAllowedStatus,
  isInstallGraceWindowOpen,
  isValidGraceDocId,
};
