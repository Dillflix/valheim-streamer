// Sample the normalized cell centres in the decoded picture. Verify both identity and CRC
// before offering a report. Never infer capture time from browser or server wall clocks.
export function readReference(getLuma) {
  const bytes = new Uint8Array(21);
  for (let bit = 0; bit < 168; bit++) {
    const luma = getLuma((1.5 + bit % 24) / 160, (82.5 + Math.floor(bit / 24)) / 90);
    if (!Number.isFinite(luma) || (luma > 90 && luma < 165)) return null;
    if (luma >= 165) bytes[bit >> 3] |= 128 >> (bit % 8);
  }
  if (bytes[0] !== 0x56 || bytes[1] !== 0x42 || bytes[2] !== 1) return null;
  let crc = 0xffff;
  for (const byte of bytes.subarray(0, 19)) {
    crc ^= byte << 8;
    for (let b = 0; b < 8; b++) crc = ((crc << 1) ^ (crc & 0x8000 ? 0x1021 : 0)) & 0xffff;
  }
  const integer = (start, end) => bytes.slice(start, end).reduce((a, b) => a * 256 + b, 0);
  if (crc !== integer(19, 21)) return null;
  const utc = new Date(integer(3, 9)).toISOString().replace(/[-:.Z]/g, '');
  return {session: utc + '-' + integer(9, 13).toString(16).padStart(8, '0'), time: integer(13, 19) / 1000};
}
export function fromVideo(video) {
  const w = video.videoWidth, h = video.videoHeight;
  if (!w || !h || video.readyState < 2 || video.seeking) return null;
  const canvas = document.createElement('canvas');
  // Copy the marker region once, retaining its native dimensions.
  const x = Math.floor(w / 160), y = Math.floor(h * 82 / 90);
  canvas.width = Math.ceil(w * 25 / 160) - x;
  canvas.height = Math.ceil(h * 89 / 90) - y;
  const ctx = canvas.getContext('2d', {willReadFrequently: true});
  ctx.drawImage(video, x, y, canvas.width, canvas.height, 0, 0, canvas.width, canvas.height);
  const pix = ctx.getImageData(0, 0, canvas.width, canvas.height).data;
  return readReference((u, v) => pix[4 * ((Math.floor(v * h) - y) * canvas.width + Math.floor(u * w) - x)]);
}
