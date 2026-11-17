import { readMinutes, totalMinutes, writeDuration, writeMinutes } from './recipe-minutes';

describe('a recipe time as hours and minutes (PP4.13)', () => {
  it.each([
    [{ hours: '', minutes: '' }, { kind: 'empty' }],
    [{ hours: ' ', minutes: ' ' }, { kind: 'empty' }],
    [
      { hours: '', minutes: '0' },
      { kind: 'minutes', value: 0 },
    ],
    [
      { hours: '0', minutes: '' },
      { kind: 'minutes', value: 0 },
    ],
    [
      { hours: '1', minutes: '35' },
      { kind: 'minutes', value: 95 },
    ],
    [
      { hours: '2', minutes: '' },
      { kind: 'minutes', value: 120 },
    ],
    [
      { hours: '', minutes: '90' },
      { kind: 'minutes', value: 90 },
    ],
    [{ hours: '1.5', minutes: '' }, { kind: 'unreadable' }],
    [{ hours: '', minutes: 'ten' }, { kind: 'unreadable' }],
    [{ hours: '-1', minutes: '' }, { kind: 'unreadable' }],
    [{ hours: '1234567', minutes: '' }, { kind: 'unreadable' }],
  ])('reads %j as %j', (time, expected) => {
    expect(readMinutes(time)).toEqual(expected);
  });

  it.each([
    [null, { hours: '', minutes: '' }],
    [0, { hours: '', minutes: '0' }],
    [45, { hours: '', minutes: '45' }],
    [95, { hours: '1', minutes: '35' }],
    [120, { hours: '2', minutes: '' }],
  ])('shows %j as %j, which reads back the same', (value, time) => {
    expect(writeMinutes(value)).toEqual(time);
    expect(readMinutes(time)).toEqual(
      value === null ? { kind: 'empty' } : { kind: 'minutes', value },
    );
  });

  it.each([
    [0, '0 min'],
    [20, '20 min'],
    [60, '1 hr'],
    [140, '2 hr 20 min'],
    [1440, '1 day'],
    [1620, '1 day 3 hr'],
    [2880, '2 days'],
    [2885, '2 days 5 min'],
  ])('writes %i minutes as the recipe card does: %j', (value, text) => {
    expect(writeDuration(value)).toBe(text);
  });

  it('totals only the times entered, zero included, and none when none was', () => {
    expect(totalMinutes([null, null, null])).toBeNull();
    expect(totalMinutes([0, null, null])).toBe(0);
    expect(totalMinutes([15, 30, null])).toBe(45);
  });
});
