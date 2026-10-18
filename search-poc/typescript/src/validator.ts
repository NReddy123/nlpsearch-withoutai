import type {
  FieldDefinition,
  FieldRegistry,
  ParsedQuery,
  ParsedValue,
  QueryError,
  QueryLimits,
  QueryResult,
  ValidatedCondition,
  ValidatedQuery
} from "./model.ts";
import { defaultLimits } from "./model.ts";

const numberPattern = /^[+-]?(?:\d+(?:\.\d*)?|\.\d+)$/u;
const datePattern = /^\d{4}-\d{2}-\d{2}$/u;

export function validateQuery(
  parsed: ParsedQuery,
  registry: FieldRegistry,
  limits: QueryLimits = defaultLimits
): QueryResult<ValidatedQuery> {
  const lookup = new Map<string, FieldDefinition>();
  for (const field of registry.fields) {
    for (const identifier of [field.name, ...field.aliases]) {
      const normalized = identifier.toLocaleLowerCase("en-US");
      if (lookup.has(normalized)) {
        throw new Error(`Duplicate field name or alias '${identifier}' in registry.`);
      }
      lookup.set(normalized, field);
    }
  }

  if (parsed.conditions.length > limits.maxConditions) {
    return fail("too_many_conditions", `Queries may contain at most ${limits.maxConditions} conditions.`, parsed.conditions[limits.maxConditions]!.start, parsed.conditions[limits.maxConditions]!.length);
  }

  const conditions: ValidatedCondition[] = [];
  for (const condition of parsed.conditions) {
    const field = lookup.get(condition.field.toLocaleLowerCase("en-US"));
    if (!field) {
      return fail("unknown_field", `Unknown search field '${condition.field}'.`, condition.fieldStart, condition.fieldLength);
    }
    if (!field.operators.includes(condition.operator)) {
      return fail("unsupported_operator", `Operator '${condition.operator}' is not allowed for '${field.name}'.`, condition.operatorStart, condition.operatorLength);
    }
    if (condition.operator === "IN" && condition.values.length > limits.maxInListSize) {
      return fail("in_list_too_large", `IN lists may contain at most ${limits.maxInListSize} values.`, condition.start, condition.length);
    }

    const converted: string[] = [];
    for (const value of condition.values) {
      const result = validateValue(value, field);
      if (!result.ok) return result;
      converted.push(result.value);
    }
    if (condition.operator === "RANGE" && compare(field.type, converted[0]!, converted[1]!) > 0) {
      const upper = condition.values[1]!;
      return fail("invalid_range", "RANGE lower bound must not exceed its upper bound.", upper.start, upper.length);
    }

    conditions.push({
      kind: condition.operator === "IN" ? "in" : condition.operator === "RANGE" ? "range" : "comparison",
      field: field.name,
      type: field.type,
      operator: condition.operator,
      values: converted,
      start: condition.start,
      length: condition.length
    });
  }
  return { ok: true, value: { conditions } };
}

function validateValue(value: ParsedValue, field: FieldDefinition): QueryResult<string> {
  if (field.type === "string") {
    if (!value.quoted) return fail("invalid_value", "String values must be double-quoted.", value.start, value.length);
    if (field.allowedValues && !field.allowedValues.includes(value.raw)) {
      return fail("invalid_value", `Value '${value.raw}' is not allowed for '${field.name}'.`, value.start, value.length);
    }
    return { ok: true, value: value.raw };
  }
  if (value.quoted) return fail("invalid_value", `${field.type} values must not be quoted.`, value.start, value.length);
  if (field.type === "number") {
    return numberPattern.test(value.raw) && isWithinDecimalRange(value.raw)
      ? { ok: true, value: value.raw }
      : fail("invalid_value", "Expected a representable signed decimal without separators or exponent notation.", value.start, value.length);
  }
  if (!datePattern.test(value.raw) || !isCalendarDate(value.raw)) {
    return fail("invalid_value", "Expected a valid calendar date in YYYY-MM-DD format.", value.start, value.length);
  }
  return { ok: true, value: value.raw };
}

function isWithinDecimalRange(value: string): boolean {
  const { whole, fraction } = decimalParts(value);
  const maxWhole = "79228162514264337593543950335";
  const wholeComparison = compareDigits(whole, maxWhole);
  return wholeComparison < 0 || (wholeComparison === 0 && /^0*$/u.test(fraction));
}

function isCalendarDate(value: string): boolean {
  const [year, month, day] = value.split("-").map(Number);
  if (year === 0) return false;
  const date = new Date(Date.UTC(year!, month! - 1, day!));
  return date.getUTCFullYear() === year && date.getUTCMonth() === month! - 1 && date.getUTCDate() === day;
}

function compare(type: FieldDefinition["type"], left: string, right: string): number {
  if (type === "number") {
    const a = decimalParts(left);
    const b = decimalParts(right);
    if (a.whole === "0" && /^0*$/u.test(a.fraction)) a.sign = 1;
    if (b.whole === "0" && /^0*$/u.test(b.fraction)) b.sign = 1;
    if (a.sign !== b.sign) return a.sign - b.sign;
    const whole = compareDigits(a.whole, b.whole) * a.sign;
    if (whole !== 0) return whole;
    const fraction = compareDigits(a.fraction.padEnd(Math.max(a.fraction.length, b.fraction.length), "0"),
      b.fraction.padEnd(Math.max(a.fraction.length, b.fraction.length), "0")) * a.sign;
    return fraction;
  }
  return left < right ? -1 : left > right ? 1 : 0;
}

function decimalParts(text: string): { sign: number; whole: string; fraction: string } {
  const negative = text.startsWith("-");
  const unsigned = text.replace(/^[+-]/u, "");
  const [whole = "0", fraction = ""] = unsigned.split(".");
  return { sign: negative ? -1 : 1, whole: whole.replace(/^0+(?=\d)/u, ""), fraction };
}

function compareDigits(a: string, b: string): number {
  const left = a.replace(/^0+(?=\d)/u, "");
  const right = b.replace(/^0+(?=\d)/u, "");
  if (left.length !== right.length) return left.length < right.length ? -1 : 1;
  return left < right ? -1 : left > right ? 1 : 0;
}

function fail<T = never>(code: string, message: string, start: number, length: number): QueryResult<T> {
  const error: QueryError = { code, message, start, length };
  return { ok: false, error };
}
