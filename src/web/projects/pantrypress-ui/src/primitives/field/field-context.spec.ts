import { TestBed } from '@angular/core/testing';
import { PpIdSequence } from './field-context';

describe('PpIdSequence', () => {
  it('starts again for every application', () => {
    const first = [
      TestBed.inject(PpIdSequence).next('field'),
      TestBed.inject(PpIdSequence).next('field'),
    ];
    TestBed.resetTestingModule();
    const second = TestBed.inject(PpIdSequence).next('field');
    expect(first).toEqual(['pp-field-1', 'pp-field-2']);
    expect(second).toBe('pp-field-1');
  });
});
