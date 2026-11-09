import fs from 'node:fs';
import assert from 'node:assert/strict';
import {readReference} from '../../tools/stream-review/static/marker.js';
const [file, width, height] = process.argv.slice(2);
const w = Number(width), h = Number(height), raw = fs.readFileSync(file);
assert.equal(raw.length, w*h*2);
for (const [frame, time] of [387.125,404.75].entries()) {
  const sample = (x,y) => raw[frame*w*h + Math.floor(y*h)*w + Math.floor(x*w)];
  assert.deepEqual(readReference(sample), {session:'20261108T193012345-abcd1234', time});
  assert.equal(readReference((x,y) => y > 88/90 && x > 24/160 ? 255-sample(x,y) : sample(x,y)), null);
}
assert.equal(readReference(() => 90), null);
