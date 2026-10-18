import {
  defaultLimits,
  type Operator,
  type ParsedCondition,
  type ParsedQuery,
  type ParsedValue,
  type QueryError,
  type QueryLimits,
  type QueryResult
} from "./model.ts";

interface Token {
  kind: "word" | "value" | "string" | "operator" | "comma" | "left" | "right" | "end";
  raw: string;
  value: string;
  start: number;
  length: number;
}

const comparisonOperators = new Set<Operator>(["=", "!=", "<", "<=", ">", ">="]);

class ParseFailure extends Error {
  readonly detail: QueryError;

  constructor(detail: QueryError) {
    super(detail.message);
    this.detail = detail;
  }
}

export function parseQuery(
  query: string,
  limits: QueryLimits = defaultLimits
): QueryResult<ParsedQuery> {
  if (query.length > limits.maxLength) {
    return failure("query_too_long", `Query exceeds ${limits.maxLength} UTF-16 code units.`, limits.maxLength, 0);
  }

  try {
    const tokens = tokenize(query);
    const conditions: ParsedCondition[] = [];
    let index = 0;
    const current = () => tokens[index]!;
    const advance = () => tokens[index++]!;
    const fail = (code: string, message: string, token: Token): never => {
      throw new ParseFailure({ code, message, start: token.start, length: token.length });
    };
    const require = (kind: Token["kind"], message: string): Token => {
      if (current().kind !== kind) fail("invalid_syntax", message, current());
      return advance();
    };

    if (current().kind === "end") {
      fail("invalid_syntax", "Enter a search condition.", current());
    }

    while (true) {
      const field = require("word", "Expected a field name.");
      const operatorToken = advance();
      const upper = operatorToken.value.toUpperCase();
      const operator: Operator | undefined = comparisonOperators.has(operatorToken.value as Operator)
        ? operatorToken.value as Operator
        : upper === "IN" || upper === "RANGE"
          ? upper
          : undefined;
      if (operator === undefined) {
        throw new ParseFailure({
          code: "invalid_syntax",
          message: "Expected a supported operator.",
          start: operatorToken.start,
          length: operatorToken.length
        });
      }

      let values: ParsedValue[];
      if (operator === "IN" || operator === "RANGE") {
        require("left", `Expected '(' after ${operator}.`);
        values = [];
        if (current().kind === "right") {
          fail("invalid_syntax", `${operator} requires ${operator === "RANGE" ? "exactly two bounds" : "at least one value"}.`, current());
        }
        while (true) {
          const value = current();
          if (value.kind !== "value" && value.kind !== "string" && value.kind !== "word") {
            fail("invalid_syntax", "Expected a list value.", value);
          }
          advance();
          values.push(toParsedValue(value));
          if (current().kind !== "comma") break;
          advance();
          if (current().kind === "right") {
            fail("invalid_syntax", "Expected a value after ','.", current());
          }
        }
        require("right", "Expected ')' after the value list.");
        if (operator === "RANGE" && values.length !== 2) {
          const offender = values[2];
          const location = offender
            ? { start: offender.start, length: offender.length }
            : tokens[index - 1]!;
          throw new ParseFailure({
            code: "invalid_syntax",
            message: "RANGE requires exactly two comma-separated bounds.",
            start: location.start,
            length: location.length
          });
        }
        if (operator === "IN" && values.length > limits.maxInListSize) {
          fail("in_list_too_large", `IN lists may contain at most ${limits.maxInListSize} values.`, field);
        }
      } else {
        const value = current();
        if (value.kind !== "value" && value.kind !== "string" && value.kind !== "word") {
          fail("invalid_syntax", "Expected a value after the comparison operator.", value);
        }
        advance();
        values = [toParsedValue(value)];
      }

      const last = tokens[index - 1]!;
      conditions.push({
        field: field.value,
        fieldStart: field.start,
        fieldLength: field.length,
        operator,
        operatorStart: operatorToken.start,
        operatorLength: operatorToken.length,
        values,
        start: field.start,
        length: last.start + last.length - field.start
      });
      if (conditions.length > limits.maxConditions) {
        fail("too_many_conditions", `Queries may contain at most ${limits.maxConditions} conditions.`, field);
      }

      if (current().kind === "end") break;
      const conjunction = current();
      if (conjunction.kind !== "word" || conjunction.value.toUpperCase() !== "AND") {
        fail("invalid_syntax", "Conditions must be joined with explicit AND.", conjunction);
      }
      advance();
      if (current().kind === "end") {
        fail("invalid_syntax", "Expected a condition after AND.", current());
      }
    }

    return { ok: true, value: { conditions } };
  } catch (error) {
    if (error instanceof ParseFailure) return { ok: false, error: error.detail };
    throw error;
  }
}

function toParsedValue(token: Token): ParsedValue {
  return { raw: token.value, quoted: token.kind === "string", start: token.start, length: token.length };
}

function failure(code: string, message: string, start: number, length: number): QueryResult<never> {
  return { ok: false, error: { code, message, start, length } };
}

function tokenize(query: string): Token[] {
  const tokens: Token[] = [];
  let index = 0;
  while (index < query.length) {
    if (/\s/u.test(query[index]!)) {
      index++;
      continue;
    }
    const start = index;
    const character = query[index]!;
    if (character === "(" || character === ")" || character === ",") {
      index++;
      tokens.push({
        kind: character === "(" ? "left" : character === ")" ? "right" : "comma",
        raw: character, value: character, start, length: 1
      });
      continue;
    }
    if (character === "=" || character === "!" || character === "<" || character === ">") {
      index++;
      if (query[index] === "=" && character !== "=") index++;
      const raw = query.slice(start, index);
      if (!comparisonOperators.has(raw as Operator)) {
        throw new ParseFailure({ code: "invalid_syntax", message: "Unsupported operator.", start, length: raw.length });
      }
      tokens.push({ kind: "operator", raw, value: raw, start, length: raw.length });
      continue;
    }
    if (character === '"') {
      index++;
      let value = "";
      while (index < query.length) {
        const next = query[index++]!;
        if (next === '"') {
          tokens.push({ kind: "string", raw: query.slice(start, index), value, start, length: index - start });
          break;
        }
        if (next === "\\") {
          if (index >= query.length || (query[index] !== '"' && query[index] !== "\\")) {
            throw new ParseFailure({
              code: "invalid_syntax",
              message: "Only \\\\ and \\\" escapes are supported in quoted strings.",
              start: index - 1,
              length: Math.min(2, query.length - (index - 1))
            });
          }
          value += query[index++]!;
        } else {
          value += next;
        }
      }
      if (tokens.at(-1)?.start !== start) {
        throw new ParseFailure({ code: "invalid_syntax", message: "Unterminated quoted string.", start, length: query.length - start });
      }
      continue;
    }

    while (index < query.length && !/[\s(),=!<>"]/u.test(query[index]!)) index++;
    if (index === start) {
      throw new ParseFailure({ code: "invalid_syntax", message: `Unexpected character '${character}'.`, start, length: 1 });
    }
    const raw = query.slice(start, index);
    const isWord = /^[A-Za-z_][A-Za-z0-9_]*$/u.test(raw);
    tokens.push({ kind: isWord ? "word" : "value", raw, value: raw, start, length: index - start });
  }
  tokens.push({ kind: "end", raw: "", value: "", start: query.length, length: 0 });
  return tokens;
}
