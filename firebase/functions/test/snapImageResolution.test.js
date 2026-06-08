"use strict";

const { describe, it } = require("node:test");
const assert = require("node:assert/strict");
const {
  snapImageResolution,
  normalizeReplicateInput,
} = require("../src/snapImageResolution");

describe("snapImageResolution", () => {
  it("returns exact allowed string enums unchanged", () => {
    assert.equal(snapImageResolution("256"), "256");
    assert.equal(snapImageResolution("512"), "512");
    assert.equal(snapImageResolution("768"), "768");
    assert.equal(snapImageResolution(512), "512");
  });

  it("snaps large mask dimensions to 768", () => {
    assert.equal(snapImageResolution(1024), "768");
    assert.equal(snapImageResolution(896), "768");
    assert.equal(snapImageResolution(900), "768");
  });

  it("snaps small values to 256", () => {
    assert.equal(snapImageResolution(100), "256");
    assert.equal(snapImageResolution(256), "256");
  });

  it("prefers higher resolution on ties", () => {
    assert.equal(snapImageResolution(384), "512");
    assert.equal(snapImageResolution(640), "768");
  });

  it("defaults invalid values to 512", () => {
    assert.equal(snapImageResolution(null), "512");
    assert.equal(snapImageResolution(""), "512");
    assert.equal(snapImageResolution("not-a-number"), "512");
  });
});

describe("normalizeReplicateInput", () => {
  it("rewrites image_resolution before forwarding", () => {
    const normalized = normalizeReplicateInput({
      prompt: "cave map",
      image_resolution: 1024,
    });
    assert.equal(normalized.image_resolution, "768");
    assert.equal(normalized.prompt, "cave map");
  });

  it("leaves input unchanged when image_resolution is absent", () => {
    const input = { prompt: "cave map" };
    assert.equal(normalizeReplicateInput(input), input);
  });
});
