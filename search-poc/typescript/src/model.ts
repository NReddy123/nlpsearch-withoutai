export type FieldType = "string" | "number" | "date";
export type Operator = "=" | "!=" | "<" | "<=" | ">" | ">=" | "IN" | "RANGE";

export interface FieldDefinition {
  name: string;
  aliases: string[];
  type: FieldType;
  operators: Operator[];
  allowedValues?: string[];
}

export interface FieldRegistry {
  version: number;
  fields: FieldDefinition[];
}

export interface QueryLimits {
  maxLength: number;
  maxConditions: number;
  maxInListSize: number;
}

export const defaultLimits: QueryLimits = {
  maxLength: 4096,
  maxConditions: 64,
  maxInListSize: 100
};

export interface QueryError {
  code: string;
  message: string;
  start: number;
  length: number;
}

export type QueryResult<T> =
  | { ok: true; value: T }
  | { ok: false; error: QueryError };

export interface ParsedValue {
  raw: string;
  quoted: boolean;
  start: number;
  length: number;
}

export interface ParsedCondition {
  field: string;
  fieldStart: number;
  fieldLength: number;
  operator: Operator;
  operatorStart: number;
  operatorLength: number;
  values: ParsedValue[];
  start: number;
  length: number;
}

export interface ParsedQuery {
  conditions: ParsedCondition[];
}

export interface ValidatedCondition {
  kind: "comparison" | "in" | "range";
  field: string;
  type: FieldType;
  operator: Operator;
  values: string[];
  start: number;
  length: number;
}

export interface ValidatedQuery {
  conditions: ValidatedCondition[];
}
