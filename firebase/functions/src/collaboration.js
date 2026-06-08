"use strict";

const { getFirestore, FieldValue } = require("firebase-admin/firestore");
const { getMessaging } = require("firebase-admin/messaging");

/**
 * @param {FirebaseFirestore.Firestore} db
 * @param {string} projectId
 * @param {string} uid
 * @returns {Promise<FirebaseFirestore.DocumentData>}
 */
async function assertProjectMember(db, projectId, uid) {
  const projectRef = db.collection("shared_projects").doc(projectId);
  const projectSnap = await projectRef.get();
  if (!projectSnap.exists) {
    const err = new Error("Shared project not found.");
    err.code = "not-found";
    throw err;
  }
  const project = projectSnap.data() || {};
  const members = project.memberUids || [];
  if (!members.includes(uid) && project.ownerUid !== uid) {
    const err = new Error("You are not a member of this shared project.");
    err.code = "permission-denied";
    throw err;
  }
  return project;
}

/**
 * @param {FirebaseFirestore.Firestore} db
 * @param {string} uid
 * @param {string} projectName
 * @param {string|null} surveyJsonUrl
 * @param {string[]} memberUids
 */
async function createSharedProject(db, uid, projectName, surveyJsonUrl, memberUids) {
  const ref = db.collection("shared_projects").doc();
  const members = Array.from(new Set([uid, ...(memberUids || [])]));
  await ref.set({
    ownerUid: uid,
    memberUids: members,
    projectName: projectName || "Untitled cave",
    surveyJsonUrl: surveyJsonUrl || null,
    updatedAtMs: Date.now(),
    createdAtMs: Date.now(),
  });
  return ref.id;
}

/**
 * @param {FirebaseFirestore.Firestore} db
 * @param {string} projectId
 * @param {string} uid
 * @param {string} displayName
 * @param {string} text
 */
async function postComment(db, projectId, uid, displayName, text) {
  const project = await assertProjectMember(db, projectId, uid);

  const projectRef = db.collection("shared_projects").doc(projectId);
  const createdAtMs = Date.now();
  await commentRef.set({
    authorUid: uid,
    authorDisplayName: displayName || uid,
    text: text.trim(),
    createdAtMs,
  });
  await projectRef.update({ updatedAtMs: createdAtMs, lastCommentAtMs: createdAtMs });

  return { commentId: commentRef.id, createdAtMs, projectName: project.projectName || "Cave project", members: project.memberUids || [] };
}

/**
 * @param {FirebaseFirestore.Firestore} db
 * @param {string} projectId
 * @param {string} uid
 */
async function listComments(db, projectId, uid) {
  await assertProjectMember(db, projectId, uid);

  const snap = await db
    .collection("shared_projects")
    .doc(projectId)
    .collection("comments")
    .orderBy("createdAtMs", "desc")
    .limit(100)
    .get();
  return snap.docs.map((d) => {
    const data = d.data();
    return {
      commentId: d.id,
      authorUid: data.authorUid,
      authorDisplayName: data.authorDisplayName || "",
      text: data.text || "",
      createdAtMs: data.createdAtMs || 0,
    };
  });
}

/**
 * @param {FirebaseFirestore.Firestore} db
 * @param {string} uid
 * @param {string} fcmToken
 * @param {string} platform
 */
async function registerDeviceToken(db, uid, fcmToken, platform) {
  const tokenId = Buffer.from(fcmToken).toString("base64url").slice(0, 120);
  await db
    .collection("user_devices")
    .doc(uid)
    .collection("tokens")
    .doc(tokenId)
    .set({
      fcmToken,
      platform: platform || "unknown",
      updatedAtMs: Date.now(),
    });
}

/**
 * @param {FirebaseFirestore.Firestore} db
 * @param {string[]} memberUids
 * @param {string} excludeUid
 */
async function collectMemberTokens(db, memberUids, excludeUid) {
  const tokens = [];
  for (const memberUid of memberUids || []) {
    if (memberUid === excludeUid) continue;
    const snap = await db.collection("user_devices").doc(memberUid).collection("tokens").limit(20).get();
    for (const doc of snap.docs) {
      const t = doc.data()?.fcmToken;
      if (typeof t === "string" && t.length > 10) tokens.push(t);
    }
  }
  return Array.from(new Set(tokens));
}

/**
 * @param {string[]} tokens
 * @param {{ title: string, body: string, projectId: string }} payload
 */
async function sendPushToTokens(tokens, payload) {
  if (!tokens.length) return { sent: 0 };
  const messaging = getMessaging();
  const res = await messaging.sendEachForMulticast({
    tokens,
    notification: { title: payload.title, body: payload.body },
    data: {
      type: "project_comment",
      projectId: payload.projectId,
    },
  });
  return { sent: res.successCount, failed: res.failureCount };
}

module.exports = {
  assertProjectMember,
  createSharedProject,
  postComment,
  listComments,
  registerDeviceToken,
  collectMemberTokens,
  sendPushToTokens,
};
