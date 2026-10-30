export interface SseFrame {
  event: string;
  data: string;
}

/**
 * Splits a Server-Sent Events text buffer into complete frames. What follows the last blank line is an unfinished frame:
 * it is returned as `rest` to be completed by the next chunk. Comment lines (": ...") and frames without data are dropped.
 */
export function parseSseFrames(buffer: string): { frames: SseFrame[]; rest: string } {
  const parts = buffer.replace(/\r\n/g, '\n').split('\n\n');
  const rest = parts.pop() ?? '';
  const frames = parts.map(parseFrame).filter((frame): frame is SseFrame => frame !== null);
  return { frames, rest };
}

function parseFrame(block: string): SseFrame | null {
  let event = 'message';
  const data: string[] = [];
  for (const line of block.split('\n')) {
    if (line.startsWith(':')) {
      continue;
    }

    const separator = line.indexOf(':');
    const field = separator === -1 ? line : line.slice(0, separator);
    const value = separator === -1 ? '' : line.slice(separator + 1).replace(/^ /, '');
    if (field === 'event') {
      event = value;
    } else if (field === 'data') {
      data.push(value);
    }
  }

  return data.length === 0 ? null : { event, data: data.join('\n') };
}
