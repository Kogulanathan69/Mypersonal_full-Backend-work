/**
 * Contract status registry. Every API operation the frontend calls must be declared here
 * with its verification status. `docs/contract-register.md` is checked against this list.
 *
 *   VERIFIED — method + path + DTOs confirmed with the backend owner (link the evidence).
 *   MOCK     — UI built against labelled demo data; NOT INTEGRATED.
 *   BLOCKED  — required but no contract exists yet; the UI shows an unavailable state.
 *
 * Phase 01 declares no operations: no backend contract has been supplied yet.
 */
export type ContractStatus = 'VERIFIED' | 'MOCK' | 'BLOCKED';

export type OwnerModule = 'F' | 'A' | 'B' | 'C';

export interface ContractEntry {
  /** Stable id, e.g. "auth.signIn". */
  readonly id: string;
  readonly owner: OwnerModule;
  readonly status: ContractStatus;
  /** Only when VERIFIED. Never guessed. */
  readonly method?: 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE';
  readonly path?: string;
  /** Where verification came from (OpenAPI file, ticket, meeting note). Required when VERIFIED. */
  readonly evidence?: string;
  readonly notes?: string;
}

const registry = new Map<string, ContractEntry>();

/** Declares an operation. Throws on duplicates or on VERIFIED entries without method, path and evidence. */
export function declareContract(entry: ContractEntry): ContractEntry {
  if (registry.has(entry.id)) {
    throw new Error(`Contract "${entry.id}" is declared twice.`);
  }
  if (entry.status === 'VERIFIED' && (!entry.method || !entry.path || !entry.evidence)) {
    throw new Error(`Contract "${entry.id}" is VERIFIED but lacks method, path or evidence.`);
  }
  if (entry.status !== 'VERIFIED' && (entry.method || entry.path)) {
    throw new Error(
      `Contract "${entry.id}" is ${entry.status}; do not record an unverified method or path.`,
    );
  }
  registry.set(entry.id, entry);
  return entry;
}

export function listContracts(): readonly ContractEntry[] {
  return [...registry.values()].sort((a, b) => a.id.localeCompare(b.id));
}

/**
 * Test helper only: removes the contracts a test declared. Never clears the whole registry,
 * because application contracts are declared once when their module loads.
 */
export function removeContractsForTesting(match: (id: string) => boolean): void {
  for (const id of [...registry.keys()]) if (match(id)) registry.delete(id);
}
