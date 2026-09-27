import assert from "node:assert/strict";
import test from "node:test";
import { thumbnailHtml } from "./thumbnail-html.ts";

const original = "https://res.cloudinary.com/demo/image/upload/v123/newsletter.png";
const optimized = width => original.replace("/upload/", `/upload/c_limit,w_${width}/q_auto/f_auto/`);

test("list thumbnails request smaller images without changing layout or links", () => {
  const html = `<a href="${original}"><img src="${original}" width="560" height="700" alt="Newsletter"></a>`;
  for (const width of [160, 640]) {
    assert.equal(thumbnailHtml(html, width), `<a href="${original}"><img src="${optimized(width)}" width="560" height="700" alt="Newsletter"></a>`);
  }
  assert.ok(html.includes(`src="${original}"`));
});

test("signed, transformed, external, animated, and local images retain their source", () => {
  const sources = [
    original.replace("/upload/", "/upload/s--signature--/"),
    original.replace("/upload/", "/upload/c_fill,w_300/"),
    `${original}?token=signed`, `${original}#fragment`,
    original.replace("res.cloudinary.com", "res.cloudinary.com.example.org"),
    original.replace(".png", ".gif"), original.replace(".png", ".svg"),
    "/images/newsletter.png", "data:image/png;base64,AAAA",
  ];
  for (const source of sources) {
    const html = `<img src="${source}">`;
    assert.equal(thumbnailHtml(html, 160), html);
  }
});

test("image source parsing preserves other attributes, quoted text, and comments", () => {
  const html = `<img alt='Use src="${original}" > here' data-src="${original}" SRC = '${original}'>`;
  assert.equal(thumbnailHtml(html, 640), `<img alt='Use src="${original}" > here' data-src="${original}" SRC = '${optimized(640)}'>`);
  assert.equal(thumbnailHtml(`<img src=${original}>`, 160), `<img src=${optimized(160)}>`);
  for (const markup of [`<!-- <img src="${original}"> -->`, `<script>const example = '<img src="${original}">';</script>`, `<style>/* <img src="${original}"> */</style>`]) {
    assert.equal(thumbnailHtml(markup, 160), markup);
  }
});

test("reprocessing a thumbnail does not stack image transformations", () => {
  const once = thumbnailHtml(`<img src="${original}">`, 640);
  assert.equal(thumbnailHtml(once, 640), once);
});
