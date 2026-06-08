"use strict";

const { initializeApp } = require("firebase-admin/app");
const { getFirestore } = require("firebase-admin/firestore");
const { onCall, HttpsError } = require("firebase-functions/v2/https");
const { defineSecret } = require("firebase-functions/params");
const { assertPremiumEntitlement } = require("./src/entitlement");
const { assertWithinRateLimit } = require("./src/rateLimit");
const { runReplicatePrediction } = require("./src/replicateProxy");
const {
  createSharedProject,
  postComment,
  listComments,
  registerDeviceToken,
  collectMemberTokens,
  sendPushToTokens,
} = require("./src/collaboration");

initializeApp();

const replicateApiToken = defineSecret("REPLICATE_API_TOKEN");

const ALLOWED_MODEL_VERSIONS = new Set([
  // jagilley/controlnet-scribble — matches ReplicateControlNetProvider.DefaultModelVersion
  "435061a1b5a4c1e26740464bf786efdfa9cb3a3ac488595a2de23e143fdb0117",
]);

/**
 * Secure Replicate proxy for CaveAI Pro Windows generative map rendering.
 *
 * Callable payload:
 *   { modelVersion: string, input: object }
 *
 * Returns:
 *   { predictionId, status, outputUrl }
 */
exports.replicateGenerativeMap = onCall(
  {
    region: "us-central1",
    secrets: [replicateApiToken],
    timeoutSeconds: 540,
    memory: "512MiB",
    maxInstances: 20,
  },
  async (request) => {
    if (!request.auth?.uid) {
      throw new HttpsError("unauthenticated", "Firebase Auth ID token required.");
    }

    const uid = request.auth.uid;
    const data = request.data || {};
    const modelVersion = typeof data.modelVersion === "string" ? data.modelVersion.trim() : "";
    const input = data.input;

    if (!modelVersion) {
      throw new HttpsError("invalid-argument", "modelVersion is required.");
    }
    if (!ALLOWED_MODEL_VERSIONS.has(modelVersion)) {
      throw new HttpsError("invalid-argument", "modelVersion is not allowed.");
    }
    if (!input || typeof input !== "object" || Array.isArray(input)) {
      throw new HttpsError("invalid-argument", "input object is required.");
    }

    const db = getFirestore();

    try {
      await assertPremiumEntitlement(db, uid);
    } catch (err) {
      throw toHttpsError(err, "permission-denied");
    }

    try {
      await assertWithinRateLimit(db, uid);
    } catch (err) {
      throw toHttpsError(err, "resource-exhausted");
    }

    const token = replicateApiToken.value();
    if (!token) {
      throw new HttpsError("failed-precondition", "Replicate API token is not configured on the server.");
    }

    try {
      return await runReplicatePrediction(token, modelVersion, input);
    } catch (err) {
      throw toHttpsError(err, "internal");
    }
  },
);

exports.shareProjectCollaboration = onCall({ region: "us-central1" }, async (request) => {
  if (!request.auth?.uid) {
    throw new HttpsError("unauthenticated", "Firebase Auth ID token required.");
  }
  const db = getFirestore();
  try {
    await assertPremiumEntitlement(db, request.auth.uid);
  } catch (err) {
    throw toHttpsError(err, "permission-denied");
  }
  const data = request.data || {};
  const projectName = typeof data.projectName === "string" ? data.projectName.trim() : "Cave project";
  const surveyJsonUrl = typeof data.surveyJsonUrl === "string" ? data.surveyJsonUrl.trim() : null;
  const memberUids = Array.isArray(data.memberUids) ? data.memberUids.filter((x) => typeof x === "string") : [];
  const projectId = await createSharedProject(db, request.auth.uid, projectName, surveyJsonUrl, memberUids);
  return { projectId };
});

exports.postProjectComment = onCall({ region: "us-central1" }, async (request) => {
  if (!request.auth?.uid) {
    throw new HttpsError("unauthenticated", "Firebase Auth ID token required.");
  }
  const db = getFirestore();
  try {
    await assertPremiumEntitlement(db, request.auth.uid);
  } catch (err) {
    throw toHttpsError(err, "permission-denied");
  }
  const data = request.data || {};
  const projectId = typeof data.projectId === "string" ? data.projectId.trim() : "";
  const text = typeof data.text === "string" ? data.text.trim() : "";
  if (!projectId) throw new HttpsError("invalid-argument", "projectId is required.");
  if (!text) throw new HttpsError("invalid-argument", "text is required.");
  const displayName = request.auth.token?.name || request.auth.token?.email || request.auth.uid;
  const result = await postComment(db, projectId, request.auth.uid, displayName, text);
  const tokens = await collectMemberTokens(db, result.members, request.auth.uid);
  const push = await sendPushToTokens(tokens, {
    title: `Comment on ${result.projectName}`,
    body: text.slice(0, 180),
    projectId,
  });
  return { commentId: result.commentId, pushSent: push.sent };
});

exports.listProjectComments = onCall({ region: "us-central1" }, async (request) => {
  if (!request.auth?.uid) {
    throw new HttpsError("unauthenticated", "Firebase Auth ID token required.");
  }
  const db = getFirestore();
  try {
    await assertPremiumEntitlement(db, request.auth.uid);
  } catch (err) {
    throw toHttpsError(err, "permission-denied");
  }
  const projectId = typeof request.data?.projectId === "string" ? request.data.projectId.trim() : "";
  if (!projectId) throw new HttpsError("invalid-argument", "projectId is required.");
  const comments = await listComments(db, projectId, request.auth.uid);
  return { comments };
});

exports.registerDeviceToken = onCall({ region: "us-central1" }, async (request) => {
  if (!request.auth?.uid) {
    throw new HttpsError("unauthenticated", "Firebase Auth ID token required.");
  }
  const fcmToken = typeof request.data?.fcmToken === "string" ? request.data.fcmToken.trim() : "";
  const platform = typeof request.data?.platform === "string" ? request.data.platform.trim() : "unknown";
  if (!fcmToken) throw new HttpsError("invalid-argument", "fcmToken is required.");
  await registerDeviceToken(getFirestore(), request.auth.uid, fcmToken, platform);
  return { ok: true };
});

function toHttpsError(err, fallbackCode) {
  if (err instanceof HttpsError) return err;
  const code = err?.code && typeof err.code === "string" ? err.code : fallbackCode;
  const message = err?.message || "Unexpected error.";
  const details = err?.details ?? undefined;
  return details != null ? new HttpsError(code, message, details) : new HttpsError(code, message);
}
