import { Pipe, PipeTransform } from '@angular/core';

export type SortDirection = 'asc' | 'desc';

export function getSortFieldValue(item: unknown, field: string): unknown {
  if (item == null || !field) {
    return undefined;
  }

  // Fast-path for non-nested fields
  if (!field.includes('.')) {
    return (item as Record<string, unknown>)[field];
  }

  const keys = field.split('.');
  let value = item;

  for (const key of keys) {
    if (value == null || typeof value !== 'object') {
      return undefined;
    }
    value = (value as Record<string, unknown>)[key];
  }

  return value;
}

function compareSortValues(left: unknown, right: unknown): number {
  if (left === right) {
    return 0;
  }

  if (left == null) {
    return 1;
  }

  if (right == null) {
    return -1;
  }

  if (typeof left === 'string' && typeof right === 'string') {
    return left.localeCompare(right, undefined, { sensitivity: 'base' });
  }

  if (left instanceof Date && right instanceof Date) {
    return left.getTime() - right.getTime();
  }

  if (typeof left === 'boolean' && typeof right === 'boolean') {
    return Number(left) - Number(right);
  }

  const comparableLeft = left as string | number | bigint;
  const comparableRight = right as string | number | bigint;

  if (comparableLeft < comparableRight) {
    return -1;
  }

  if (comparableLeft > comparableRight) {
    return 1;
  }

  return 0;
}

export function sortItems<T>(
  array: readonly T[],
  field: string,
  order: SortDirection = 'asc',
  accessor: (item: T, field: string) => unknown = getSortFieldValue,
): T[] {
  if (!Array.isArray(array)) {
    return [];
  }

  const direction = order === 'asc' ? 1 : -1;

  // Utilize Schwartzian transform for O(N) accessor evaluation rather than O(N log N)
  return array
    .map((item) => ({ item, value: accessor(item, field) }))
    .sort((left, right) => compareSortValues(left.value, right.value) * direction)
    .map((wrapper) => wrapper.item);
}

@Pipe({ name: 'sort' })
export class SortPipe implements PipeTransform {
  transform(array: unknown[], field: string, order: SortDirection = 'asc'): unknown[] {
    return sortItems(array, field, order);
  }
}
