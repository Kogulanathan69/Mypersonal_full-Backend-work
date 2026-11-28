import { declareContract, listContracts, removeContractsForTesting } from './contract-status';

const mine = () => listContracts().filter((c) => c.id.startsWith('spec.'));

describe('contract registry', () => {
  afterEach(() => removeContractsForTesting((id) => id.startsWith('spec.')));

  it('lists declared contracts sorted by id', () => {
    declareContract({ id: 'spec.b', owner: 'A', status: 'MOCK' });
    declareContract({ id: 'spec.a', owner: 'F', status: 'BLOCKED' });
    expect(mine().map((c) => c.id)).toEqual(['spec.a', 'spec.b']);
  });

  it('rejects duplicate ids', () => {
    declareContract({ id: 'spec.x', owner: 'F', status: 'MOCK' });
    expect(() => declareContract({ id: 'spec.x', owner: 'F', status: 'MOCK' })).toThrow(
      /declared twice/,
    );
  });

  it('requires method, path and evidence for VERIFIED contracts', () => {
    expect(() =>
      declareContract({ id: 'spec.v', owner: 'F', status: 'VERIFIED', method: 'GET' }),
    ).toThrow(/lacks/);
  });

  it('forbids recording a guessed path on unverified contracts', () => {
    expect(() =>
      declareContract({ id: 'spec.m', owner: 'A', status: 'MOCK', method: 'GET', path: '/guess' }),
    ).toThrow(/do not record/);
  });

  it('accepts a fully evidenced VERIFIED contract', () => {
    const entry = declareContract({
      id: 'spec.ok',
      owner: 'F',
      status: 'VERIFIED',
      method: 'GET',
      path: 'health',
      evidence: 'openapi.json rev 1',
    });
    expect(mine()).toEqual([entry]);
  });
});
