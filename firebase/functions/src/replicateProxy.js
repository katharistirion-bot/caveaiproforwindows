"use strict";

const { normalizeReplicateInput } = require("./snapImageResolution");

const REPLICATE_API_ROOT = "https://api.replicate.com/v1/";
const DEFAULT_POLL_MS = 2000;
const DEFAULT_TIMEOUT_MS = 8 * 60 * 1000;

/**
 * @param {string} apiToken
 * @param {string} modelVersion
 * @param {Record<string, unknown>} input
 * @param {(status: string) => void} [onStatus]
 */
async function runReplicatePrediction(apiToken, modelVersion, input, onStatus) {
  const normalizedInput = normalizeReplicateInput(input);
  const createResp = await fetch(`${REPLICATE_API_ROOT}predictions`, {
    method: "POST",
    headers: {
      Authorization: `Bearer ${apiToken}`,
      "Content-Type": "application/json",
    },
    body: JSON.stringify({ version: modelVersion, input: normalizedInput }),
  });

  const createBody = await createResp.text();
  if (!createResp.ok) {
    const err = new Error(`Replicate create failed (${createResp.status}): ${trimBody(createBody)}`);
    err.code = createResp.status === 401 || createResp.status === 403 ? "internal" : "unavailable";
    throw err;
  }

  let prediction;
  try {
    prediction = JSON.parse(createBody);
  } catch {
    const err = new Error("Replicate returned invalid JSON.");
    err.code = "internal";
    throw err;
  }

  const id = prediction?.id;
  if (!id) {
    const err = new Error("Replicate did not return a prediction id.");
    err.code = "internal";
    throw err;
  }

  const deadline = Date.now() + DEFAULT_TIMEOUT_MS;
  while (true) {
    if (Date.now() > deadline) {
      const err = new Error("Replicate prediction timed out.");
      err.code = "deadline-exceeded";
      throw err;
    }

    const getResp = await fetch(`${REPLICATE_API_ROOT}predictions/${id}`, {
      headers: { Authorization: `Bearer ${apiToken}` },
    });
    const getBody = await getResp.text();
    if (!getResp.ok) {
      const err = new Error(`Replicate poll failed (${getResp.status}): ${trimBody(getBody)}`);
      err.code = "unavailable";
      throw err;
    }

    let current;
    try {
      current = JSON.parse(getBody);
    } catch {
      const err = new Error("Replicate poll returned invalid JSON.");
      err.code = "internal";
      throw err;
    }

    const status = (current?.status || "").toLowerCase();
    if (status === "succeeded") {
      const outputUrl = extractFirstOutputUrl(current?.output);
      if (!outputUrl) {
        const err = new Error("Replicate prediction succeeded but returned no output URL.");
        err.code = "internal";
        throw err;
      }
      return {
        predictionId: id,
        status: current.status,
        outputUrl,
      };
    }

    if (status === "failed" || status === "canceled") {
      const err = new Error(current?.error || `Replicate prediction ${current?.status}.`);
      err.code = "internal";
      throw err;
    }

    onStatus?.(current?.status || "processing");
    await sleep(DEFAULT_POLL_MS);
  }
}

function extractFirstOutputUrl(output) {
  if (typeof output === "string") return output;
  if (Array.isArray(output) && typeof output[0] === "string") return output[0];
  return null;
}

function trimBody(body, max = 400) {
  if (!body) return "";
  return body.length <= max ? body : `${body.slice(0, max)}…`;
}

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

module.exports = {
  runReplicatePrediction,
  extractFirstOutputUrl,
};
