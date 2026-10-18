import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import { parseQuery, validateQuery, type FieldRegistry } from "../src/index.ts";

const root = new URL("../../shared/", import.meta.url);
const registry = JSON.parse(readFileSync(new URL("field-registry.json", root), "utf8")) as FieldRegistry;
const fixtures = JSON.parse(readFileSync(new URL("parser-fixtures.json", root), "utf8")) as {
  valid: { name: string; query: string; expected: unknown[] }[];
  invalid: { name: string; query: string; code: string; start: number; length: number }[];
};

for (const fixture of fixtures.valid) {
  test(`shared fixture: ${fixture.name}`, () => {
    const parsed = parseQuery(fixture.query);
    assert.equal(parsed.ok, true);
    if (!parsed.ok) return;
    const validated = validateQuery(parsed.value, registry);
    assert.equal(validated.ok, true);
    if (!validated.ok) return;
    assert.deepEqual(validated.value.conditions.map(({ kind, field, type, operator, values }) =>
      ({ kind, field, type, operator, values })), fixture.expected);
  });
}

for (const fixture of fixtures.invalid) {
  test(`shared invalid fixture: ${fixture.name}`, () => {
    const parsed = parseQuery(fixture.query);
    const result = parsed.ok ? validateQuery(parsed.value, registry) : parsed;
    assert.equal(result.ok, false);
    if (result.ok) return;
    assert.deepEqual(
      { code: result.error.code, start: result.error.start, length: result.error.length },
      { code: fixture.code, start: fixture.start, length: fixture.length }
    );
  });
}

test("uses UTF-16 code-unit offsets for astral characters", () => {
  const parsed = parseQuery('customer = "😀" AND unknown = "x"');
  assert.equal(parsed.ok, true);
  if (!parsed.ok) return;
  const validated = validateQuery(parsed.value, registry);
  assert.equal(validated.ok, false);
  if (validated.ok) return;
  assert.equal(validated.error.start, 20);
  assert.equal(validated.error.length, 7);
});

test("limits are configurable", () => {
  const result = parseQuery('amount=1 AND amount=2', { maxLength: 4096, maxConditions: 1, maxInListSize: 10 });
  assert.equal(result.ok, false);
  if (!result.ok) assert.equal(result.error.code, "too_many_conditions");
});

test("IN list limit is configurable and enforced", () => {
  const parsed = parseQuery('status IN ("Open", "Pending", "Closed")', {
    maxLength: 4096,
    maxConditions: 64,
    maxInListSize: 2
  });
  assert.equal(parsed.ok, false);
  if (!parsed.ok) assert.equal(parsed.error.code, "in_list_too_large");
});
