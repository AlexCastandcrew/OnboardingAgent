import { parseSseFrames } from './sse';

describe('parseSseFrames', () => {
  it('parseSseFrames_TwoCompleteFrames_ReturnsBothInOrderWithNoRest', () => {
    const { frames, rest } = parseSseFrames('event: activity\ndata: {"a":1}\n\nevent: response\ndata: {"b":2}\n\n');

    expect(frames).toEqual([{ event: 'activity', data: '{"a":1}' }, { event: 'response', data: '{"b":2}' }]);
    expect(rest).toBe('');
  });

  it('parseSseFrames_ChunkEndsInTheMiddleOfAFrame_KeepsTheUnfinishedPartAsRest', () => {
    const first = parseSseFrames('event: activity\ndata: {"a":1}\n\nevent: resp');
    const second = parseSseFrames(first.rest + 'onse\ndata: {"b":2}\n\n');

    expect(first.frames.length).toBe(1);
    expect(first.rest).toBe('event: resp');
    expect(second.frames).toEqual([{ event: 'response', data: '{"b":2}' }]);
  });

  it('parseSseFrames_CommentsCrLfAndMultiLineData_AreHandled', () => {
    const { frames } = parseSseFrames(': keep-alive\r\n\r\nevent: error\r\ndata: line one\r\ndata: line two\r\n\r\n');

    expect(frames).toEqual([{ event: 'error', data: 'line one\nline two' }]);
  });

  it('parseSseFrames_FrameWithoutEventName_IsAMessage', () => {
    expect(parseSseFrames('data: hello\n\n').frames).toEqual([{ event: 'message', data: 'hello' }]);
  });
});
