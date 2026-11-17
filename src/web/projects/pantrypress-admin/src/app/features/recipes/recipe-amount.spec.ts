import { readAmount, writeAmount } from './recipe-amount';

const amount = (numerator: number, denominator: number) => ({
  kind: 'amount',
  value: { numerator, denominator },
});

describe('a yield amount as typed (PP4.13)', () => {
  it.each([
    ['8', amount(8, 1)],
    [' 12 ', amount(12, 1)],
    ['1.5', amount(3, 2)],
    ['.25', amount(1, 4)],
    ['0.125', amount(1, 8)],
    ['3/4', amount(3, 4)],
    ['3⁄4', amount(3, 4)],
    ['6/4', amount(3, 2)],
    ['1 1/2', amount(3, 2)],
    ['2  2/3', amount(8, 3)],
    ['½', amount(1, 2)],
    ['1½', amount(3, 2)],
    ['1 ½', amount(3, 2)],
    ['2⅔', amount(8, 3)],
    ['999999999', amount(999_999_999, 1)],
  ])('reads %j exactly, in lowest terms', (text, expected) => {
    expect(readAmount(text)).toEqual(expected);
  });

  it.each(['', '   '])('reads %j as nothing typed', (text) => {
    expect(readAmount(text)).toEqual({ kind: 'empty' });
  });

  it.each([
    'eight',
    '1,5',
    '1/2/3',
    '1 1/2 3',
    '1-1/2',
    '½½',
    'a½',
    '1.2.3',
    '1.1234567',
    '1234567890',
    '٣',
    '1 1.5',
    '-2',
    '999999999 1/999999999',
  ])('leaves %j unread, for the person to fix', (text) => {
    expect(readAmount(text)).toEqual({ kind: 'unreadable' });
  });

  it('passes zero on, so the server says why', () => {
    expect(readAmount('0')).toEqual(amount(0, 1));
    expect(readAmount('1/0')).toEqual(amount(1, 0));
  });

  it.each([
    [8, 1, '8'],
    [1, 2, '½'],
    [3, 2, '1½'],
    [8, 3, '2⅔'],
    [7, 8, '⅞'],
    [37, 16, '2 5/16'],
    [5, 16, '5/16'],
  ])('writes %i/%i as %j, and reads it back the same', (numerator, denominator, text) => {
    expect(writeAmount({ numerator, denominator })).toBe(text);
    expect(readAmount(text)).toEqual(amount(numerator, denominator));
  });
});
